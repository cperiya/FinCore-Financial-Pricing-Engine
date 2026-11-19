# Architecture and request flow

## Current implementation

FinCore is a modular monolith: one API host with separate projects for HTTP,
application orchestration, domain rules, and integrations. The Milestone 1
stock valuation endpoint calculates:

```text
MarketValue = UnitPrice * Quantity
```

The calculation uses `decimal`, accepts fractional quantities, requires a
non-empty symbol and positive numeric inputs, and does not apply rounding or
currency conversion. The price and result are assumed to share the same
caller-known currency.

The Domain project also contains USD-only portfolio entities and a
trade-derived, long-only position calculation. The Application use cases and
REST endpoints for portfolios, instruments, trades, positions, simulated
market prices, and portfolio market valuation are implemented. EF Core 10
SQL Server adapters and the initial migration are implemented; a fresh local
development database must be migrated explicitly. See the
[database design](database-design.md) and
[README local database instructions](../README.md#local-sql-server-2022-development-environment).

## Projects and dependency direction

| Project | Current responsibility | Project references |
| --- | --- | --- |
| `FinCore.Api` | HTTP controllers, request validation, response mapping, dependency-injection composition, Problem Details, and Development OpenAPI document | Application, Infrastructure |
| `FinCore.Application` | Stock-pricing, portfolio, and portfolio-valuation use cases, repository contracts, and transaction contract | Domain |
| `FinCore.Domain` | `StockValuationService`, USD portfolio entities, and long-only position calculation | None |
| `FinCore.Infrastructure` | EF Core context, SQL Server mappings and repositories, migrations, and DI registration | Application, Domain |
| `FinCore.UnitTests` | xUnit domain tests and in-memory API integration tests | API, Application, Domain |
| `FinCore.IntegrationTests` | Opt-in SQL Server persistence and HTTP integration tests | API, Infrastructure |
| `FinCore.Benchmarks` | SQL-only valuation measurements, validation, and offline cache-feasibility experiments | Application, Infrastructure |

```text
FinCore.Api ───────────────> FinCore.Application ──> FinCore.Domain
     │                              ↑
     └──> FinCore.Infrastructure ───┘
                      └───────────────────────────> FinCore.Domain

FinCore.UnitTests ────────> FinCore.Application
       └─────────────────> FinCore.Domain
       └─────────────────> FinCore.Api (root endpoint integration test)
FinCore.IntegrationTests ─> FinCore.Api ─> Application, Infrastructure
                         └──────────────────> Infrastructure, Domain
FinCore.Benchmarks ───────> FinCore.Application, FinCore.Infrastructure
```

Arrows are compile-time project references. The API registers Infrastructure
and portfolio Application services when `ConnectionStrings:FinCore` is
configured. The stock valuation and root discovery endpoints remain available
without a database connection.

### Design rationale

- **Keep the Domain dependency-free.** Valuation and portfolio rules do not
  need ASP.NET Core, EF Core, a database, or an external service. That keeps
  the rules directly testable and protects them from transport or storage
  changes.
- **Put orchestration in Application.** `StockPricingService` is the use case
  boundary. It calls the domain rule and returns an application result rather
  than making the API calculate the value itself.
- **Keep HTTP concerns in API.** The controller binds query parameters,
  validates request DTOs, maps Application results to response DTOs, and
  returns HTTP status codes. Centralized exception handling returns safe
  Problem Details. The domain does not know about HTTP.
- **Use an application interface.** `IStockPricingService` allows the API to
  depend on the use-case contract and leaves the application service
  replaceable in tests and future hosts.
- **Do not add an interface for the simple domain calculation.** The
  stateless domain service has no integration or alternate implementation
  seam at this point; keeping it concrete avoids an abstraction without a
  present use.
- **Use `decimal` for money-like values.** Decimal arithmetic avoids binary
  floating-point representation artifacts for these inputs. The API does not
  impose currency-specific scale or rounding; it returns the exact
  representable decimal product, or reports overflow.
- **Keep one deployable application.** Project separation makes the code
  navigable and testable without introducing microservice deployment or
  network boundaries.

Infrastructure owns EF Core and SQL Server dependencies. Application exposes
persistence contracts without a provider dependency, and Domain remains
independent of EF Core and SQL Server. The API uses the Infrastructure
registration extension only when a connection string is configured. The local
Compose database does not affect the existing stock-valuation request flow.

### Cryptography dependency-version safeguard

`FinCore.Infrastructure` keeps an explicit
`System.Security.Cryptography.Xml` 9.0.21 reference to constrain the resolved
dependency graph; Infrastructure source does not call its APIs directly.
Removing the reference selected vulnerable transitive
`System.Security.Cryptography.Xml` 9.0.0 and
`System.Security.Cryptography.Pkcs` 9.0.4, with NU1902/NU1903 restore
warnings. With the safeguard retained, Infrastructure resolves Xml 9.0.21
and Pkcs 9.0.21, while `FinCore.Api` separately pins Pkcs 9.0.23. The API pin
is unchanged, and the current solution-wide vulnerability audit is clean.

## Portfolio persistence model and transaction boundary

`FinCoreDbContext` maps `Portfolio`, `Instrument`, `Trade`, and `MarketPrice`.
`Position` is derived from trades and is not stored. Required relationships
connect portfolios and instruments to trades, and instruments to market-price
history. Foreign-key deletes are restricted to preserve trade and price
history. Currency/type/side, positive decimal, and UTC timestamp constraints
are mapped to the database alongside domain constructor validation.

Financial columns use `decimal(28,10)`. `FinCoreDbContext` rejects added or
modified trade and market-price values outside that range or scale before
save, so SQL Server cannot silently round them. UTC values use
`datetimeoffset(7)` and database checks require an offset of zero. The
[database design guide](database-design.md) shows the entity relationships,
indexes, and migration status.

`TradeApplicationService` loads portfolio/instrument history, appends the
candidate, and asks `PositionCalculationService` to validate the entire
chronological sequence before adding the trade. Backdated trades are permitted
only when the complete resulting history remains long-only. Equal timestamps
retain the Domain ordering of execution time, BUY before SELL, then trade ID.

Every BUY and SELL calls `IUnitOfWork.ExecuteWithExclusiveLocksAsync`.
Infrastructure starts a SQL Server `SERIALIZABLE` transaction and acquires
transaction-owned exclusive `sp_getapplock` resources
`FinCore:Trade:{tradeId:N}` and
`FinCore:Position:{portfolioId:N}:{instrumentId:N}` in ordinal order, before
any trade-history lookup. Lock acquisition, repository reads, `SaveChanges`,
and commit use the same scoped `FinCoreDbContext` and transaction. A second
SELL against the same position waits; after the first commits, the second
reloads history and is rejected if the new position is insufficient. The
Domain check alone is not the concurrency mechanism.

The lock timeout is 15 seconds and becomes a surfaced `TimeoutException`;
SQL Server application-lock deadlock/error result codes become
`TransactionLockAcquisitionException`. SQL deadlock error 1205 is rolled back
and surfaced as `TransactionDeadlockException`. SQL Server TCP wait timeout
error 258 is translated to `TransientPersistenceException` only when its
severity is at least 20; this translation also covers transaction startup.
Command timeouts remain `TimeoutException`. Authentication, invalid SQL, and
other unclassified SQL failures propagate instead of being marked retryable.
Rollback failures are combined with the original error rather than hidden.
Cancellation is passed through lock acquisition and persistence; canceled SQL
commands are surfaced as `OperationCanceledException`. Uncommitted work is
rolled back with a non-cancelable rollback attempt and transaction disposal.
The service does not automatically retry database failures. A caller may
safely retry a trade with the same trade ID.

The caller-supplied `Trade.Id` is the idempotency key and existing primary-key
uniqueness constraint. Under the trade-ID application lock, the service
returns the stored trade for an identical canonical payload; a different
payload with that ID raises `TradeIdempotencyConflictException`. Decimal
equality treats equivalent scales as the same amount, and Domain validation
requires UTC-offset-zero timestamps before comparison. No migration is needed.

## Portfolio valuation read flow

`PortfolioValuationApplicationService` owns the valuation use case. It
captures one effective UTC cutoff (the supplied `asOfUtc`, or one call to
`TimeProvider.GetUtcNow()`), then uses
`IUnitOfWork.ExecuteReadOnlyInTransactionAsync` to run sequential, no-tracking
repository reads in one SQL Server `SERIALIZABLE` transaction on the scoped
`FinCoreDbContext`. The read-only transaction commits without calling
`SaveChangesAsync`.

The trade query filters `ExecutedAt <= cutoff` and retains Domain chronology:
execution time, BUY before SELL, then `Trade.Id`. The Domain
`PositionCalculationService` derives positive quantities and omits closed
positions. If there are no open holdings, the service returns an empty list
and a zero USD total without fetching instruments or prices. Otherwise, it
fetches all held instruments and the latest eligible price per instrument in
bulk, then calls `StockValuationService.CalculateMarketValue` for each
position and adds the results with checked decimal arithmetic. Repository
queries are split into bounded batches to remain below SQL Server's parameter
limit; EF operations on the shared context are sequential, never concurrent.

Price eligibility is inclusive (`MarketPrice.AsOfUtc <= cutoff`); the
repository orders by `AsOfUtc DESC, Id DESC`, so the greatest ID wins a
same-time tie. No stale-price threshold is defined or enforced. The response
includes the selected price timestamp, and prices after the cutoff are
excluded. If any open position lacks an eligible price, the Application
service throws `MissingMarketPricesException` containing every affected ID
and symbol; the API returns `409` without a partial valuation. Checked
overflow is represented as `PortfolioValuationOverflowException` and
returned as `422`.

Serializable range locks prevent a valuation from combining incompatible
trade and price states, but can block concurrent writes. SQL lock contention
can increase latency and a deadlock is possible; SQL Server error 1205 is
rolled back by the existing unit of work and returned as a sanitized retryable
`503`. Recognized SQL TCP wait timeouts (error 258, severity 20+) at
transaction startup or during reads also map to sanitized `503`. This read
transaction does not use application locks and does not
change the trade-write locking/idempotency protocol.

The Application unit tests cover portfolio creation, instrument normalization,
trade recording, backdated and equal-time history, duplicate submissions,
eligible price selection, and portfolio valuation. SQL Server integration
tests exercise concurrency, rollback/retry, cancellation, lock timeout, and
valuation behavior using the existing run-owned database fixture. Prompt 5A previously passed 54 unit tests and 14
SQL tests on each of two fresh-database runs.

Prompt 5B verification passed the solution build with 0 warnings and 0 errors,
62 unit/in-memory HTTP tests, and 17 SQL Server integration tests in each of
two fresh fixture runs. The HTTP integration tests verified the resolved
`FinCoreDbContext` database name before requests. The vulnerability audit found
no vulnerable packages.

Prompt 6 verification passed the build with 0 warnings and 0 errors. The full
solution run passed 78 unit/in-memory HTTP tests and skipped 20 opt-in SQL
tests (0 failures). The isolated SQL integration project passed 20 tests on
each of two fresh run-owned database runs, with 0 failures and 0 skips. The
valuation tests cover historical cutoffs, deterministic price ties,
concurrent trade-and-price consistency, and the HTTP result of 80 MSFT shares
at USD 450 = USD 36,000. The NuGet vulnerability audit reported no vulnerable
packages and `git diff --check` was clean.

The publication-audit follow-up separates unrestricted legacy stock-symbol
normalization from the 20-character registered-instrument constraint. Its
latest build had 0 warnings and errors, all 79 unit/in-memory HTTP tests
passed. The full run also passed the fixture endpoint guardrail test and
skipped the 20 opt-in SQL tests. Two isolated SQL runs each passed all 20
tests with no failures or skips. The SQL test fixture requires
`127.0.0.1,1433` for the local Windows Docker configuration.

The `20261115200739_InitialPortfolioPersistence` EF Core migration was applied
to a local `FinCore` development database; fresh installations must apply it
explicitly. That local schema was checked for the four
tables, configured column types, indexes, foreign keys, delete behavior, and
check constraints. SQL Server integration tests use a fresh,
GUID-named database per fixture run, apply the migration with
`MigrateAsync`, and drop only that database after validating its ownership
marker. They use the existing `FinCoreIntegrationTests` connection only as a
credential source and do not open or modify that database.

## SQL-only performance experiments

The separate [`FinCore.Benchmarks`](../benchmarks/FinCore.Benchmarks/) project
references Application and Infrastructure, not the API host. BenchmarkDotNet
measures service calls and managed allocations for synthetic portfolio
workloads. Other runner modes measure sequential end-to-end service latency,
SQL reader timing, stage diagnostics, and matched scalar/JSON translations.
Provisioning and seeding are outside timed calls. Reader timing stops at
reader availability; the full price stage includes EF materialization.
First-client calls are not cold SQL measurements, and no shared SQL caches
are cleared. See [performance benchmarking](performance-benchmarking.md) for
workloads, sample counts, results, commands, and limitations.

The benchmark fixture validates the local SQL Server 2022 Developer endpoint,
creates a uniquely named `FinCoreBenchmarkRun_<GUID>` database, records an
owner marker, applies the existing migration, and seeds synthetic data.
Lifecycle locks and exact-name/marker checks protect cleanup; if ownership
cannot be verified, it refuses to drop the database. Interrupted-run recovery
also requires the matching ignored manifest. It never uses `FinCore` or
`FinCoreIntegrationTests` as benchmark write targets or removes Docker
volumes.

`PortfolioValuationApplicationService` accepts an optional
`IPortfolioValuationDiagnostics` sink and otherwise uses a no-op stage.
The benchmark-only collector and SQL interceptor are registered only in
benchmark-created providers, not normal API composition. Instrumentation
adds overhead; SQL reader time and approximate process-wide allocation
deltas must not be interpreted as uninstrumented per-call measurements.

`PortfolioCollectionParameterOptions.UseJsonParameterForIds` defaults to
`false`, so the instrument and latest-price ID batches retain scalar
translation. An explicitly configured experimental provider can opt into
query-local `EF.Parameter` JSON translation; no API configuration binding
enables it automatically. Both modes retain the existing batch size and
valuation rules. The 50-instrument JSON price-stage regression remains
unexplained, so the default has not changed.

The SQL-only cache-feasibility runner records selected price identities and
uses an offline reuse oracle; its estimates are not Redis hit rates or
measured Redis savings. Production Redis integration is deferred pending
representative workload and consistency evidence. Parallel valuation was
considered, but has not been implemented or benchmarked: EF operations remain
sequential on the scoped context.

## Stock valuation request flow

1. A client sends `GET /api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4`.
2. ASP.NET Core routes the request to `PricingController.GetStock`.
3. `[ApiController]` binds the query into `StockValuationRequest`. Required
   fields and its `IValidatableObject` implementation reject missing or blank
   symbols and numeric values that are missing, malformed, zero, or negative
   with `400 Bad Request` validation details.
4. The controller calls `IStockPricingService.ValueStock` with the validated
   symbol and numeric inputs.
5. `StockPricingService` calls
   `StockValuationService.CalculateMarketValue`. It trims and uppercases the
   symbol, while the domain method independently enforces positive numeric
   inputs and calculates `unitPrice * quantity`.
6. The application service packages the normalized symbol, inputs, and
   calculated value in `StockValuationResult`.
7. The controller maps the application result to `StockValuationResponse` and
   returns `200 OK`. If decimal multiplication overflows, it returns a
   validation-problem response with status `400`.

Request and response models are API types; the application result is a separate
application type. Neither HTTP types nor currency conversion logic leak into
Domain.

## Portfolio and trading HTTP request flow

The thin `PortfoliosController` and `InstrumentsController` actions call the
existing Application services; they do not calculate positions, enforce
long-only history, normalize persisted symbols independently, or implement
transactions.

```text
HTTP request
  → ASP.NET Core model binding and request DTO validation
  → Controller maps the validated request to an Application service call
  → Application service applies the use case and calls repository contracts
  → Infrastructure repositories and unit of work use the scoped FinCoreDbContext
  → SQL Server executes reads/writes in the configured transaction boundary
  → Domain entities and PositionCalculationService enforce financial invariants
  → Controller maps Domain/Application values to API response DTOs
  → HTTP 200/201 response or ProblemDetails/ValidationProblemDetails
```

Portfolio creation returns `201 Created` with a `Location` for
`GET /api/portfolios/{id}`. The requested minimal route set has no exact GET
resource endpoint for an instrument, trade, or individual price, so those POST
actions return `200 OK`. Trade POST returns the same status for a new trade and
an exact replay because `RecordTradeAsync` returns the stored `Trade` without
an inserted-versus-replayed flag.

The trade action requires the stable caller-supplied `tradeId`. The Application
service preserves the SQL Server `SERIALIZABLE` transaction, transaction-owned
`sp_getapplock` locks, complete chronological validation, and idempotent
duplicate handling described above. A controller never performs a read-then-
insert or in-memory lock. Exact retries return the stored trade; conflicting
payloads and oversells map to `409`. Known lock timeouts, lock acquisition
failures, deadlocks, command timeouts, and recognized SQL TCP connection
timeouts map to `503` with `Retry-After: 2` and endpoint-neutral detail. For a
trade retry, clients should use the same trade ID and identical payload.
Unexpected exceptions
return a generic `500` Problem Details response; SQL messages and stack traces
are logged server-side and are not included in the response.

DTO validation rejects missing/empty identifiers, unsupported string enum
values, non-USD currencies, nonpositive or unrepresentable `decimal(28,10)`
financial values, and timestamps whose offset is not zero. `[ApiController]`
returns model-binding and annotation errors as `400 ValidationProblemDetails`.
Malformed route GUIDs return 400 because route templates do not impose a
`:guid` constraint. Missing resources return 404; duplicate symbols,
insufficient holdings, and conflicting trade IDs return 409.

## Portfolio valuation HTTP request flow

1. A client requests `GET /api/portfolios/{id}/valuation`, optionally
   supplying `asOfUtc` with a zero UTC offset. Invalid GUIDs or supplied
   non-UTC/malformed cutoffs return `400`.
2. `PortfoliosController.GetValuation` calls
   `IPortfolioValuationApplicationService`; it does not calculate holdings,
   select prices, or multiply values.
3. `PortfolioValuationApplicationService` captures one effective cutoff,
   verifies the portfolio, and opens the read-only serializable unit-of-work
   transaction.
4. The trade repository loads only rows executed by the cutoff. The Domain
   position calculator applies its established chronological ordering and
   omits closed positions.
5. For open positions, Infrastructure batch-loads instrument metadata and
   each latest eligible price. The Application service reuses
   `StockValuationService`, performs checked decimal summation, and either
   returns the complete USD valuation or reports all instruments with no
   eligible price.
6. The controller maps the Application result to immutable response DTOs.
   Missing portfolios map to `404`, missing prices to `409` with
   `missingInstruments`, arithmetic overflow to `422`, and known SQL
   deadlocks to sanitized `503`. No partial valuation or SQL details are
   returned.

## Root discovery endpoint

`HomeController.Get` handles `GET /` and returns the application name, a short
description, and relative links. It always includes the stock valuation URI
template, including the required `symbol` query parameter. It includes
`/openapi/v1.json` only in Development, matching
`Program.cs`, where `MapOpenApi` is registered only for that environment.
`ApiHomeEndpointTests` uses `WebApplicationFactory<Program>` to send actual
in-memory HTTP requests in Development and Production and assert which links
are present.

### What `MapGet` does

`MapGet(pattern, handler)` is an ASP.NET Core minimal-API method. It directly
maps an HTTP GET route pattern to a delegate, for example a lambda that
returns a JSON value. It is not used for this root route: `Program.cs` already
registers controllers and calls `MapControllers`, and the pricing endpoint is
also a controller. Keeping the root in `HomeController` maintains one endpoint
style rather than mixing controller and minimal-API routing.

A Web API does not automatically have a homepage. ASP.NET Core serves only
routes the application maps (plus any separately configured static files or
default pages); a root route is not generated just because the server is
running. Before `GET /` was added, no mapped route matched the root, so
ASP.NET Core correctly returned `404 Not Found`.

### Currency and precision assumptions

No currency code is accepted or returned. The caller must interpret `unitPrice`
and `marketValue` in one shared currency. The service does not check the
currency, select a default currency, convert currencies, or round to a
currency's minor unit. Decimal multiplication is exact for products
representable by `decimal`; an out-of-range product raises `OverflowException`
in Domain and is translated by the API to a client validation response.

## Dependency injection and API documentation

`Program.cs` registers `StockValuationService` and
`PositionCalculationService` as singletons because they are stateless, and
application use-case services as scoped services. When
`ConnectionStrings:FinCore` is set in configuration, it also calls
`AddFinCoreInfrastructure` to register the SQL Server context, repositories,
and unit of work. The connection string is not stored in `appsettings.json`.
The API uses ASP.NET Core's built-in OpenAPI support and maps
`/openapi/v1.json` only in the Development environment.

The API, Application, Domain, Infrastructure, and test projects generate XML
documentation files during build. Public types and members include XML
comments; see the [class and method reference](class-method-reference.md).

## Important files

| File | Purpose |
| --- | --- |
| [`src/FinCore.Api/Program.cs`](../src/FinCore.Api/Program.cs) | Registers controllers, OpenAPI, and application/domain services; configures middleware, routes, and host startup. |
| [`src/FinCore.Api/Controllers/PricingController.cs`](../src/FinCore.Api/Controllers/PricingController.cs) | Defines `GET /api/pricing/stock`, invokes the application service, and maps the result to HTTP. |
| [`src/FinCore.Api/Controllers/PortfoliosController.cs`](../src/FinCore.Api/Controllers/PortfoliosController.cs) | Defines portfolio creation/list/retrieval, derived-position retrieval, portfolio valuation, and transaction-safe trade recording endpoints. |
| [`src/FinCore.Api/Controllers/InstrumentsController.cs`](../src/FinCore.Api/Controllers/InstrumentsController.cs) | Defines equity registration and simulated market-price record/latest-price endpoints. |
| [`src/FinCore.Api/Middleware/ApiExceptionHandler.cs`](../src/FinCore.Api/Middleware/ApiExceptionHandler.cs) | Maps expected validation, not-found, conflict, and transient transaction exceptions to safe Problem Details responses. |
| [`src/FinCore.Api/Models/Portfolios/`](../src/FinCore.Api/Models/Portfolios/) | Defines portfolio, position, and trade request/response DTOs. |
| [`src/FinCore.Api/Models/Instruments/`](../src/FinCore.Api/Models/Instruments/) | Defines equity and market-price request/response DTOs. |
| [`src/FinCore.Api/Controllers/HomeController.cs`](../src/FinCore.Api/Controllers/HomeController.cs) | Defines the root discovery endpoint and exposes environment-appropriate links. |
| [`src/FinCore.Api/Models/ApiHomeResponse.cs`](../src/FinCore.Api/Models/ApiHomeResponse.cs) | Defines the JSON response for the root discovery endpoint. |
| [`src/FinCore.Api/Models/StockValuationRequest.cs`](../src/FinCore.Api/Models/StockValuationRequest.cs) | Defines required symbol and positive numeric query inputs, with model validation. |
| [`src/FinCore.Api/Models/StockValuationResponse.cs`](../src/FinCore.Api/Models/StockValuationResponse.cs) | Defines the JSON response contract. |
| [`src/FinCore.Application/Pricing/IStockPricingService.cs`](../src/FinCore.Application/Pricing/IStockPricingService.cs) | Application use-case contract used by the API. |
| [`src/FinCore.Application/Pricing/StockPricingService.cs`](../src/FinCore.Application/Pricing/StockPricingService.cs) | Calls the Domain calculation and creates the application result. |
| [`src/FinCore.Application/Pricing/StockValuationResult.cs`](../src/FinCore.Application/Pricing/StockValuationResult.cs) | Carries normalized symbol, unit price, quantity, and calculated market value across the application boundary. |
| [`src/FinCore.Domain/Valuation/StockValuationService.cs`](../src/FinCore.Domain/Valuation/StockValuationService.cs) | Enforces positive numeric inputs and performs decimal multiplication. |
| [`src/FinCore.Application/Portfolios/`](../src/FinCore.Application/Portfolios/) | Defines async repository contracts for portfolios, instruments, trades, and market prices. |
| [`src/FinCore.Application/Persistence/IUnitOfWork.cs`](../src/FinCore.Application/Persistence/IUnitOfWork.cs) | Defines commit and serializable transaction boundaries without EF Core types. |
| [`src/FinCore.Infrastructure/Persistence/FinCoreDbContext.cs`](../src/FinCore.Infrastructure/Persistence/FinCoreDbContext.cs) | Defines SQL Server entity sets and applies explicit mappings. |
| [`src/FinCore.Infrastructure/Persistence/Configurations/`](../src/FinCore.Infrastructure/Persistence/Configurations/) | Maps keys, precision, UTC/currency/type checks, foreign keys, delete behavior, and query indexes. |
| [`src/FinCore.Infrastructure/Persistence/Repositories/`](../src/FinCore.Infrastructure/Persistence/Repositories/) | Implements Application repository contracts using asynchronous EF Core queries. |
| [`src/FinCore.Infrastructure/Persistence/Migrations/`](../src/FinCore.Infrastructure/Persistence/Migrations/) | Contains the initial migration and its model snapshot; fresh development databases require explicit migration. |
| [`tests/FinCore.UnitTests/StockValuationServiceTests.cs`](../tests/FinCore.UnitTests/StockValuationServiceTests.cs) | Covers valid inputs, zero/negative rejection, precision, and overflow. |
| [`tests/FinCore.UnitTests/ApiHomeEndpointTests.cs`](../tests/FinCore.UnitTests/ApiHomeEndpointTests.cs) | Verifies the root route and available links through in-memory HTTP requests. |
| [`tests/FinCore.IntegrationTests/SqlServerPersistenceTests.cs`](../tests/FinCore.IntegrationTests/SqlServerPersistenceTests.cs) | Verifies SQL round trips, latest-price lookup, and rollback in a separate test database. |
| [`tests/FinCore.IntegrationTests/PortfolioApiIntegrationTests.cs`](../tests/FinCore.IntegrationTests/PortfolioApiIntegrationTests.cs) | Verifies REST workflows, idempotency, overselling, and concurrent sells through HTTP against the fixture-owned database. |
| [`src/FinCore.Api/FinCore.Api.http`](../src/FinCore.Api/FinCore.Api.http) | Provides sample successful and invalid endpoint requests. |
| [`docs/api-documentation.md`](./api-documentation.md) | Documents the public HTTP contract and currency assumptions. |
| [`docs/class-method-reference.md`](./class-method-reference.md) | Explains public FinCore types, members, parameters, and returns. |

## Validation commands

Run from the repository root in PowerShell:

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

The solution tests cover domain rules and in-memory HTTP behavior. SQL Server
integration tests are opt-in and use the separate database described in the
[README](../README.md#sql-server-integration-tests).

## Not implemented

Quote retrieval from live market data, stale-price threshold enforcement,
currency conversion, currency-specific rounding, Redis, Azure, and CI/CD are
not implemented. The Domain portfolio model supports USD and equity
instruments only. Financial examples must use synthetic data.
