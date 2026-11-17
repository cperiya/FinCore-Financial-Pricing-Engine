# Learning journal

## Milestone 1 – Solution and architecture scaffold

**Implemented:** created and registered the .NET 10 solution structure with an
ASP.NET Core API host, Application, Domain, and Infrastructure projects, plus
an xUnit test project. Added references to establish the intended clean
dependency direction, a .NET `.gitignore`, and baseline project documentation.
The API host registers controller support and configures its HTTP middleware
pipeline, but no controllers or routes are present. Expanded the README and
architecture guide to explain the current project responsibilities, startup
flow, generated files, and design rationale without describing future
functionality as implemented.

**Engineering decisions:** use a modular monolith to keep deployment simple
while separating HTTP, use cases, domain rules, and integrations. Keep Domain
independent; make the API the future composition root. Application and
Infrastructure point inward to Domain; tests reference Application and Domain
without coupling to Infrastructure. Remove the template WeatherForecast
controller and model so they are not mistaken for financial functionality.
Keep the generated `.http` scratch file unchanged and explicitly document
that its WeatherForecast request is stale.

**Not implemented:** financial pricing or portfolio-risk logic, database
access, caching, Docker, Azure resources, or CI/CD. No real financial data is
used. The xUnit project currently has no test cases.

**What to learn next:** trace the `Program.cs` host setup from builder
creation, through service registration and middleware/endpoint configuration,
to `Run`. Understand how project references differ from runtime calls, why
business rules should not depend on ASP.NET or persistence, and how the
solution and project files drive restore/build/test. When behavior is
introduced, add a focused test that exercises the behavior rather than
mistaking successful compilation for a business-rule test.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

Restore and build validate package resolution and compilation. Test invokes
the configured xUnit runner; currently it reports no available tests because
no test cases have been implemented.

## Milestone 2 – Stock position market valuation

**Implemented:** added `StockValuationService` in Domain to calculate
`MarketValue = UnitPrice * Quantity` with `decimal`. Added non-negative input
validation while allowing zero values and fractional share quantities. Added
the Application pricing use case and result, and exposed them through
`GET /api/pricing/stock`. The API returns a validation problem for missing,
malformed, or negative query values and for a product outside the `decimal`
range. Added Development-only built-in OpenAPI output, XML documentation
generation, endpoint examples, and domain unit tests.

**Currency assumption:** the caller knows the currency. Unit price and market
value share that currency; the request/response have no currency code and do
not convert or round to a currency-specific scale.

**Engineering decisions:** keep validation and multiplication in the
dependency-free Domain service so callers cannot bypass the business rule.
Use an Application interface as the API's dependency, and keep HTTP request
and response models separate from the Application result. Use built-in
ASP.NET Core OpenAPI without adding Swagger UI or an external OpenAPI tool.

**What to learn:** follow the request from query-string model binding and
`[ApiController]` validation, through dependency injection and the
Application use case, to the Domain calculation and back through response
mapping. Compare decimal multiplication with binary floating-point, inspect
how overflow is surfaced as a client error, and review how XML comments and
endpoint metadata appear in the generated Development OpenAPI document.

**Not implemented:** currency identification or FX, currency-specific
rounding, market data, portfolio aggregation, persistence, SQL Server, Entity
Framework Core, Redis, Docker, Azure, and CI/CD. Unit tests cover the domain
calculation; they do not exercise HTTP routing or OpenAPI generation.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Milestone 3 – Root API discovery endpoint

**Implemented:** added `GET /`, which returns the FinCore application name,
description, a templated link to stock valuation, and a link to the OpenAPI
document only in Development. Added an in-memory HTTP integration test for
Development and Production behavior, exposed the top-level `Program` type to
the test host, documented the endpoint, and added a root request to the
`.http` examples.

**Engineering decisions:** the API already uses attribute-routed controllers
and maps them with `MapControllers`. The root is another controller action
rather than a `MapGet` minimal API route, avoiding a second endpoint style.
The OpenAPI link follows the same Development-only rule as
`app.MapOpenApi()`, so the response does not advertise an endpoint unavailable
in Production.

**What to learn:** `MapGet` maps a GET route pattern directly to a handler
delegate in the minimal-API style; it is not the same as `MapControllers`,
which maps attribute-routed controller actions. ASP.NET Core does not create a
homepage automatically: applications serve only configured routes, and
there is no root route unless one is mapped. Study
`WebApplicationFactory<Program>` as a way to verify HTTP behavior without
starting a real network listener.

**Not implemented:** a browser-based homepage or UI. The root response is a
JSON API discovery document; stock valuation and Development OpenAPI remain
the only advertised resources.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Milestone 4 – Required stock symbol and positive valuation inputs

**Implemented:** the stock valuation contract now requires a non-blank
`symbol`, `unitPrice > 0`, and `quantity > 0`. Missing/invalid values return
HTTP 400. The Application use case trims and uppercases valid symbols, and
the successful response includes that normalized symbol. Domain validation
now also rejects zero numeric values, so the positive-input rule is preserved
when the domain service is called outside HTTP. Updated the root discovery
link, endpoint examples, XML comments, and API/class-reference documentation.

**Engineering decisions:** keep the symbol as application/API data because the
valuation formula does not use it, while normalize it at the application
boundary so all callers receive consistent results. Enforce positive numeric
inputs in Domain as well as validating the HTTP request, preventing alternate
callers from bypassing the financial rule. Test the HTTP contract using
`WebApplicationFactory<Program>` rather than only testing controller methods.

**What to learn:** compare HTTP model validation with domain invariants:
request-model validation provides useful 400 responses, while domain
validation keeps core rules reliable for every caller. Review why a symbol is
trimmed and uppercased using invariant casing, and trace the symbol from query
binding through the application result to the response DTO.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

## Portfolio Management Milestone 2 – Domain foundation

**Implemented:** added USD-only `Portfolio`, `Instrument`, `Trade`, and
`MarketPrice` entities; `TradeSide` and `InstrumentType` enums; normalized
equity symbols; and a domain service that derives positive holdings from
chronologically ordered trades. Trades are ordered by execution time, then
buy-before-sell, then trade ID for deterministic equal-time behavior. Buy
quantities increase holdings, sells decrease them, zero holdings are omitted,
and a sell that would create a negative holding raises
`InsufficientPositionException`. Entity constructors validate identifiers,
non-blank names/symbols, positive prices and quantities, the supported
USD/equity scope, and UTC timestamps. Added focused xUnit tests for entity
validation, ordering, grouping, zero positions, overselling, null input, and
decimal overflow.

**Engineering decisions:** keep Domain free of EF Core and persistence
dependencies. Use `decimal` for trade prices, market prices, and share
quantities. Normalize symbols with `Trim().ToUpperInvariant()` and expose the
normalizer so application callers can share the same rule. Calculate positions
in execution-time order, then by side (buys before sells), then by trade ID so
equal-time ordering is stable regardless of input order.
Keep database transaction and concurrent-sell protection in Application and
Infrastructure rather than Domain. Database-level protection against
concurrent trade requests will be implemented in a later application and
persistence stage; this in-memory domain calculation does not synchronize
requests. `StockValuationService` remains responsible for price-times-quantity
market value; it is not needed to sum share quantities.

**Not implemented:** portfolio repository interfaces or use cases, EF Core
and SQL Server persistence, transaction/concurrency handling, API endpoints,
market-price selection, portfolio market valuation, migrations, Docker, and
SQL Server integration tests. These remain later parts of the portfolio
management milestone.

**What to learn:** compare entity-level validation with cross-trade
invariants. A `Trade` can validate its own fields, while the position service
needs the surrounding trade history to validate long-only holdings. The
application and persistence layers will need to perform that check within a
safe transaction to prevent concurrent sells from overselling.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet build .\FinCore.slnx
dotnet test .\FinCore.slnx
```

## Portfolio Management Milestone 2 – Local SQL Server development environment

**Implemented:** added a root Docker Compose configuration for the official
SQL Server 2022 Linux image using Developer Edition, a loopback-only host port
binding, and a named persistent volume. The SA password is required through
an untracked local `.env`; `.env.example` contains only a replace-before-use
placeholder. Expanded `.gitignore` for environment variants, local secrets,
database backups/dumps, and local Docker data. Documented startup, shutdown,
connection, troubleshooting, WSL 2 setup, and the purpose of each Compose
property.

**Engineering decisions:** keep the database optional and local to Docker
Desktop, and expose its port only on `127.0.0.1`. The named volume preserves
SQL Server files across container recreation; `docker compose down` preserves
it, while removing volumes would erase local database state. Do not print or
commit the SA password. The Compose service does not add EF Core, migrations,
tables, or an application connection.

**Not implemented:** application persistence, EF Core, migrations, tables,
database repositories, API database configuration, or SQL Server integration
tests.

**Validation commands** (run in PowerShell from the repository root):

```powershell
docker compose config --quiet
docker compose up -d
docker compose ps
docker compose logs --tail 100 sqlserver
```

## Portfolio Management Milestone 2 – Application contracts and persistence

**Implemented:** added EF-free Application contracts for portfolio,
instrument, trade, and market-price repositories, plus a unit-of-work
contract. Added EF Core 10 SQL Server Infrastructure with `FinCoreDbContext`,
explicit entity configurations, repositories, dependency-injection
registration, environment-based connection-string configuration, and an
initial migration with a model snapshot. SQL mappings include `decimal(28,10)`
financial values, `datetimeoffset(7)` UTC timestamps, USD/equity/side/value
checks, foreign keys with restricted deletes, and indexes for trade history
and latest-price lookup. Infrastructure checks trade and market-price values
against SQL precision and scale before save instead of allowing silent
rounding. Added opt-in SQL Server integration tests for persistence round
trips, latest-price selection, and transaction rollback.

**Engineering decisions:** keep EF Core references inside Infrastructure;
Application contracts accept Domain types and cancellation tokens only.
Repositories track additions and leave commit boundaries to `IUnitOfWork`.
The Infrastructure unit of work offers a serializable transaction boundary
for a future sell workflow. The upcoming application service must load the
relevant trade history, calculate the candidate chronological position, and
insert within that single transaction. Domain-only validation does not stop
concurrent oversells; serializable range locks prevent both requests from
committing against stale holdings, while any deadlock requires retrying the
whole use case or returning a concurrency failure.

At the time this persistence implementation milestone was documented, the
migration was generated for review but **not yet applied** to the local
`FinCore` development database. Integration tests use a distinct
`FinCoreIntegrationTests` database and `EnsureCreated`; they do not apply the
development migration or delete the test database or Docker volume.
Connection strings are read from environment configuration and are never
checked into source.

**Not implemented:** applying the initial migration (pending schema
approval), portfolio application use cases and HTTP endpoints, a trading
service that invokes the serializable boundary, full concurrent-sell
protection, portfolio market valuation, and live market data. Database
integration tests require the optional test connection variable.

**What to learn:** compare repository contracts with provider-specific
implementations; follow an entity configuration into the generated migration
and snapshot; inspect how SQL Server checks enforce supported currency,
positive values, and UTC offsets; and trace why a trade's cross-record
long-only rule must run inside a transaction rather than only in a domain
service.

**Validation commands** (run in PowerShell from the repository root):

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

For the SQL Server integration project, set
`FINCORE_SQLSERVER_TEST_CONNECTION_STRING` to the dedicated test database
connection as shown in the [README](../README.md#sql-server-integration-tests),
then run:

```powershell
dotnet test .\tests\FinCore.IntegrationTests\FinCore.IntegrationTests.csproj --filter "Category=Integration"
```

## Portfolio persistence schema refinement and migration application

**Implemented:** aligned Domain validation, EF Core mappings, the migration,
and SQL Server schema on `Portfolio.Name` and `Instrument.Name` at 200
characters and `Instrument.Symbol` at 20 characters. Symbols are trimmed and
uppercased invariantly before persistence, and the equity-only instrument
table enforces symbol uniqueness. The regenerated
`20261115200739_InitialPortfolioPersistence` migration was applied to the
local `FinCore` database after checking that no migration had previously
been applied there. The EF model check reported no pending changes.

**Schema verification:** SQL Server confirmed all four application tables,
`decimal(28,10)` financial values, `datetimeoffset(7)` timestamps, 11 check
constraints, three foreign keys with `NO_ACTION` deletion, and the migration
history entry. The unique symbol index is `UX_Instruments_Symbol`. Trade
history uses `(PortfolioId ASC, InstrumentId ASC, ExecutedAt ASC, Side ASC,
Id ASC)`, matching equality filters followed by deterministic chronological
ordering. Latest-price retrieval uses `(InstrumentId ASC, AsOfUtc DESC,
Id DESC)`, matching its instrument filter and descending latest-price order.
The existing `FinCoreIntegrationTests` database and Docker volume were not
modified.

**Validation:** `dotnet build .\FinCore.slnx` succeeded with zero errors and
four existing NU1902/NU1903 warnings for transitive
`System.Security.Cryptography.Pkcs` 9.0.4. `dotnet test .\FinCore.slnx`
passed all 47 unit tests; the four opt-in SQL Server tests were skipped
because the test connection variable was not supplied, avoiding writes to
`FinCoreIntegrationTests`.

**Engineering decisions:** keep the database migration scoped to the local
development database and keep the opt-in integration-test database separate.
Verify schema metadata directly after applying a migration; a successful EF
command alone does not demonstrate that the expected indexes and constraints
exist. Continue to treat Domain position validation and serializable
transaction protection as separate concerns.

**Not implemented:** portfolio HTTP workflows, the trade application service
that invokes the serializable validation-and-insert boundary, full
concurrent-sell handling, market-data integration, and portfolio market
valuation. No migration was applied to the integration-test database.

## Persistence integration and package security verification

**Verified SQL tests:** supplied the test connection only through the process
environment, reading the SA password from the ignored `.env`, and ran the
four xUnit tests filtered by `Category=Integration`. Results: 4 passed, 0
failed, 0 skipped. The connection targeted `FinCoreIntegrationTests`; the
`FinCore` development database was not used. No connection string or
credential was printed.

**Test database finding:** before the run, `FinCoreIntegrationTests` had the
four entity tables and existing rows, but no `__EFMigrationsHistory` and no
`UX_Instruments_Symbol` index; duplicate `MSFT` and `TEST` symbols were
present. `EnsureCreatedAsync` does not evolve a schema when tables already
exist, so it did not bring this database up to the current EF model. The
tests left data behind: counts changed from 2 to 3 portfolios, 4 to 6
instruments, 2 to 3 trades, and 6 to 9 market prices. This database remains
separate from `FinCore` and was neither dropped nor reset.

**Package audit:** `dotnet list .\FinCore.slnx package --vulnerable
--include-transitive` found one vulnerable resolved package:
`System.Security.Cryptography.Pkcs` 9.0.4 in the `FinCore.Api` graph. It
arrives through `Microsoft.Data.SqlClient` 6.1.1 and through EF design-time
dependencies (`Microsoft.Build.Tasks.Core` and Roslyn MSBuild workspaces).
The four build warnings are the two findings below repeated during restore
and build, not four distinct vulnerable packages:

- NU1903 / [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx),
  CVE-2026-84008 (High): affected 9.0 versions through 9.0.20; patched in
  9.0.21.
- NU1902 / [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7),
  CVE-2026-70162 (Moderate): affected 9.0 versions through 9.0.20; patched
  in 9.0.21.

NuGet lists `System.Security.Cryptography.Pkcs` 9.0.23, a same-major patch
above the advisory fix floor and the 9.0.4 minimum requested by the SQL
client dependency.

**Remediation and validation:** after approval, added one direct
`System.Security.Cryptography.Pkcs` 9.0.23 package reference to
`FinCore.Api.csproj`, the only project reported with vulnerable packages.
This pins the API's inherited SQL-client and EF design-time dependency paths;
Domain, Application, Infrastructure, and test project references were not
added or changed. `dotnet restore .\FinCore.slnx` succeeded, and package
listing resolved the API package to 9.0.23 while preserving
`Microsoft.Data.SqlClient` 6.1.1 and EF Core 10.0.0. A fresh solution-wide
`dotnet list .\FinCore.slnx package --vulnerable --include-transitive`
reported no vulnerable packages in any project.

`dotnet build .\FinCore.slnx` succeeded with 0 warnings and 0 errors.
`dotnet test .\FinCore.slnx` passed 47 unit tests, with 0 failed and the four
SQL tests skipped because the test connection variable was deliberately
unset. The four SQL tests had separately passed against
`FinCoreIntegrationTests` before this package-only change. The final solution
test run did not connect to either database; no database setup or contents
were changed during the package update.

**Migration-testing decision:** keep repository behavior tests and migration
verification separate. `EnsureCreated` is suitable only when creating a
fresh schema for tests; it is not a migration runner and does not update
existing tables. Migration verification should use a separate empty test
database, call `MigrateAsync`, and assert the migration-history row and
schema objects. Do not drop or reset the existing test database without
approval.

**Changes:** the approved package pin is the only dependency change. No test
setup was modified. README, database design, class reference, and this
journal record the verified persistence findings; this entry records the
remediation and its verification.

## SQL integration-test isolation and migration verification

**Implemented:** replaced `EnsureCreatedAsync` in SQL integration tests with
an xUnit collection fixture that creates a fresh `FinCoreTestRun_<GUID>`
database for each test run and applies
`20261115200739_InitialPortfolioPersistence` using `MigrateAsync`. Tests
sharing the fixture are serialized, and each run receives an independently
generated database name.

**Safety controls:** the fixture accepts only the configured loopback SQL
Server endpoint on port 1433 with `FinCoreIntegrationTests` as the connection
string's catalog. It rewrites that catalog to `master` before opening any
connection, then confirms SQL Server 2022 Developer Edition and sysadmin
permission before database creation or cleanup. It refuses to reuse an
existing generated name and records a database-level ownership marker with
the current run ID. Cleanup validates the exact generated name and marker
before dropping only that database. If ownership cannot be verified, it
refuses the drop and raises a clear error. No broad name matching is used for
destructive commands.

**Coverage:** retained the persistence round-trip, latest-price, decimal
precision, and transaction rollback tests. Added migration/schema metadata
checks for the migration-history row, all three restricted foreign keys, all
11 check constraints, the unique symbol index, and exact trade/latest-price
index key ordering. Added tests proving Domain-normalized duplicate symbols
are rejected by SQL Server and raw SQL writes violating currency and positive
market-price constraints are rejected.

**Verification:** two consecutive SQL integration runs each passed all 7
tests with 0 failed and 0 skipped; each used a new run-owned database and
cleanup left 0 `FinCoreTestRun_` databases. The first implementation attempt
exposed assertion type/casing mistakes; those were corrected before the two
passing runs. The complete unit-test project passed 47 tests, with 0 failed
and 0 skipped. The solution build succeeded with 0 warnings and 0 errors.
The solution-wide dependency audit remains clean, and the API resolves
`System.Security.Cryptography.Pkcs` 9.0.23.

**Protected database checks:** read-only SQL verification found the local
`FinCore` database still has the initial migration history entry and 0 rows
in each application table. `FinCoreIntegrationTests` row counts matched
their pre-run values (3 portfolios, 6 instruments, 3 trades, 9 market
prices). The fixture uses neither database as a test target; no database
volume was deleted.

**Engineering decision:** `EnsureCreated` can create a schema only for a
database without tables; it neither applies nor advances migrations for an
existing database. Migration correctness must therefore be tested against a
new isolated database using `MigrateAsync`, with cleanup authorized only by
the run-specific ownership marker.

## Portfolio Management Milestone 2 – Application use cases and transaction-safe trading

**Implemented:** added Application services for portfolio create/retrieve/list
and derived positions, normalized USD-equity registration, trade recording,
and simulated USD market-price record/as-of retrieval. Extended repository
contracts and SQL Server implementations for portfolio listing, normalized
symbol lookup, trade-ID lookup, and latest eligible price. Registered the
Application services with Infrastructure in the API composition root only
when the persistence connection is configured. Preserved the existing stock
valuation flow and Domain entities.

**Financial and transaction decisions:** every BUY and SELL goes through a
single serializable unit-of-work transaction on the same scoped DbContext used
by all repositories. Before reading trade history, Infrastructure acquires
transaction-owned exclusive SQL Server application locks on
`FinCore:Trade:{tradeId:N}` and
`FinCore:Position:{portfolioId:N}:{instrumentId:N}`, sorted with ordinal
comparison. This serializes all trade sides for a position and serializes
same-ID retries. A 15-second SQL application-lock timeout is surfaced as
`TimeoutException`; application-lock result failures and SQL Server deadlocks
are surfaced as explicit Application transaction exceptions. Other SQL
failures propagate. The operation attempts rollback with a non-cancelable
token and disposes the transaction; rollback errors are reported with the
original failure. Cancellation is translated to `OperationCanceledException`
when SqlClient reports a canceled lock command. No automatic retry is
performed; the caller can retry safely with the same trade ID after an
ambiguous commit.

Backdated candidates are allowed only after the entire affected trade history
is recalculated. The Domain order remains execution timestamp, BUY before
SELL, then trade ID, including equal timestamps; any intermediate oversell
rejects the trade. A caller-supplied GUID is both `Trade.Id` and the
idempotency key. Identical canonical payloads return the stored trade without
reinsertion; reusing the ID with a different payload raises an explicit
conflict. The existing trade primary key enforces database uniqueness, so no
migration or schema adjustment was needed. Decimal equality treats equivalent
scales as equal, and Domain constructors enforce UTC-offset-zero timestamps.

**Verification:** `dotnet build .\FinCore.slnx` succeeded with 0 warnings and
0 errors. The complete solution test run passed 54 unit tests, with 0 failed;
14 SQL tests were skipped because the opt-in connection setting was absent.
The SQL Server integration project was run twice with the secure local
configuration; each fresh fixture-owned database run passed 14 tests, with 0
failed and 0 skipped. The new SQL tests covered concurrent sells, backdated
history, equal timestamps, idempotency, save failure rollback and same-ID
retry, cancellation, and lock timeout. The dependency vulnerability audit
reported no vulnerable packages, and `git diff --check` passed.

**Database safety:** the fixture cleaned up both run-owned databases. A
read-only post-run query found 0 remaining `FinCoreTestRun_` databases.
`FinCore` retained 0 portfolio, instrument, trade, and market-price rows;
`FinCoreIntegrationTests` retained 3 portfolios, 6 instruments, 3 trades,
and 9 market prices. No Docker volume was deleted, no credentials were
printed, and no existing database was targeted for integration writes.

**Not implemented:** portfolio HTTP endpoints and request/response DTOs,
portfolio market-value aggregation, live market data, and automatic retries
for deadlock or other transient database failures. SQL error 1205 is
explicitly surfaced after rollback; callers may retry using the same trade
ID.

## Prompt 5B — Portfolio and Trading REST API

**Milestone:** Expose the existing Application workflows over ASP.NET Core
controllers without moving financial rules into the HTTP layer.

**Engineering decisions:** Added `PortfoliosController` and
`InstrumentsController` with immutable request/response DTOs and
`CancellationToken` forwarding. Controllers call the existing Application
services for portfolios, instruments, trades, positions, and simulated
prices. Request validation enforces USD-only currencies, string-valued trade
sides, positive `decimal(28,10)` values that can be stored without rounding,
non-empty GUIDs, and zero-offset UTC timestamps. Added centralized
Problem Details handling: invalid requests `400`, missing resources `404`,
business conflicts `409`, known SQL lock/deadlock failures `503` with
`Retry-After`, and generic sanitized `500` responses.

Portfolio creation returns `201 Created` with a `Location` for its GET route.
Other POSTs return `200 OK`, including trade replays, because this approved
minimal API has no exact item GET routes for those resources and the current
`RecordTradeAsync` result does not distinguish an insert from a replay.
The stable caller-supplied `tradeId` remains the idempotency key; clients retry
transient failures with the same ID and unchanged payload. No Domain entities,
migrations, database schemas, or package versions were changed. The
IntegrationTests project reuses the already referenced ASP.NET Core testing
package and points HTTP requests only at the fixture-generated database,
asserting the resolved database name before making requests.

**Verification:** `dotnet build .\FinCore.slnx --no-restore` succeeded with
0 warnings and 0 errors. The complete unit and in-memory HTTP suite passed
62 tests, with 0 failed or skipped. The SQL Server integration suite passed
17 tests on each of two fresh run-owned databases, with 0 failures and 0
skips per run. These HTTP SQL tests verified the full portfolio/instrument/
trade/position/price flow, exact replay, conflicting duplicate, overselling,
and two concurrent seven-share sells against ten shares; exactly one
concurrent sell succeeded. The NuGet vulnerability audit reported no
vulnerable packages, and `git diff --check` passed.

**At the end of Prompt 5B:** Portfolio market-value aggregation and a
valuation endpoint remained; Prompt 6 below implements those items. Live
quotes, FX conversion, and item GET endpoints for instruments, trades, or
individual price records remain outside this milestone. Automatic transient
database retries are also not implemented; clients may retry trade requests
safely with the same `tradeId`.

## Portfolio Management Milestone 2 — Prompt 6 portfolio market valuation

**Implemented:** added `IPortfolioValuationApplicationService` and
`PortfolioValuationApplicationService`, together with Application result
records for the complete valuation and individual positions. Added the
`GET /api/portfolios/{id}/valuation?asOfUtc=...` endpoint and documented
response DTOs. Each position response includes instrument ID, normalized
symbol, quantity, selected USD price and observation time, and market value;
the portfolio response includes USD currency, effective cutoff, positions,
and total.

**Financial and query decisions:** the Application service captures exactly
one cutoff per request, using the supplied UTC timestamp or
`TimeProvider.GetUtcNow()`. It requests only trades executed at or before that
cutoff and relies on `PositionCalculationService` for the existing
`ExecutedAt`, BUY-before-SELL, then `Trade.Id` ordering. Closed positions are
omitted. For every open position, Infrastructure selects the latest price no
later than the same cutoff, breaking equal-time ties by greatest price ID.
`StockValuationService` performs checked decimal multiplication; the
portfolio sum is checked as well. Values are not rounded.

If any open position lacks an eligible price, the operation reports every
missing ID and symbol and returns no partial total (`409`). An empty or fully
closed portfolio returns no positions and a zero total. There is no configured
stale-price threshold: older eligible prices are returned with their
timestamps; future-dated prices are excluded. Checked overflow maps to
sanitized `422`.

**Consistency and performance:** the new read-only unit-of-work operation
uses the existing scoped `FinCoreDbContext` and a SQL Server `SERIALIZABLE`
transaction, but does not call `SaveChangesAsync`. Sequential no-tracking
queries read cutoff-filtered trades, instruments, and grouped latest prices.
Instrument IDs are processed in batches of at most 2,000 to remain under
SQL Server's parameter limit. Serializable range locks can block concurrent
trade or price writes and may cause deadlocks; the existing rollback path
surfaces known deadlocks as sanitized retryable `503`. No trade-write lock,
Domain entity, migration, schema, or package version was changed. The
existing stock valuation service was made explicitly `checked` without
changing its validation or response behavior.

**Verification:** `dotnet build .\FinCore.slnx --no-restore` succeeded with
0 warnings and 0 errors. The full solution run passed 78 unit/in-memory HTTP
tests, failed 0, and skipped 20 opt-in SQL tests. The SQL integration project
passed 20 tests on each of two fresh run-owned database runs, with 0 failures
and 0 skips in either run. Coverage includes empty portfolios, 100 shares
bought and 20 sold, multiple instruments, closed positions, historical
cutoffs, equal-time price ID ties, all missing prices, multiplication and
sum overflow, cutoff clock capture, cancellation, SQL consistency during a
trade-and-price commit, and the HTTP result of 80 shares at USD 450 = USD
36,000. The NuGet vulnerability audit found no vulnerable packages, and
`git diff --check` was clean.

**Remaining:** live market data, FX conversion, stale-price enforcement, and
automatic transient database retries are not implemented. The current
valuation endpoint uses only the synthetic USD prices already recorded in
FinCore.

## Final Milestone 2 readiness corrections

**Implemented:** corrected the test count to 78, added the API's explicit
trusted-local-development/no-authentication warning, and clarified that stable
`tradeId` reuse is required for trade retries while the shared `503` message
is endpoint-neutral. Added a provider-neutral transient persistence exception
and a narrow SQL Server translator for deadlock 1205, command timeout -2, and
TCP wait timeout 258 only at severity 20 or higher. Transaction-startup and
transaction-work failures use the same translation; cancellation, rollback,
and safe disposal behavior remain intact. Added focused translation and
sanitized HTTP-response assertions.

**Concurrency-test decision:** replaced the fixed delay in the valuation
consistency test with command-interceptor barriers. The valuation pauses after
its trade query, and the writer pauses when its insert is attempted; bounded
waits fail the test clearly instead of relying on scheduler timing or hanging.
No production transaction semantics, schema, migration, or package version
changed.

**Verification:** `dotnet build .\FinCore.slnx` succeeded with 0 warnings and
0 errors. The full solution run passed all 78 unit/in-memory HTTP tests and
skipped the 20 opt-in SQL tests because its connection variable was unset.
Two subsequent isolated SQL runs each passed all 20 tests with 0 failures
and 0 skips. The NuGet audit found no vulnerable packages and
`git diff --check` passed. Read-only SQL checks after both runs found
`FinCore` unchanged at 0 portfolios, instruments, trades, and market prices;
`FinCoreIntegrationTests` remained at 3 portfolios, 6 instruments, 3 trades,
and 9 market prices; no `FinCoreTestRun_<GUID>` databases remained.

## Publication audit follow-up — legacy symbols and local SQL endpoint

**Implemented:** introduced `StockSymbolNormalizer` to share trimming and
invariant-uppercase behavior without imposing a persistence limit.
`StockPricingService` now uses it directly, preserving Milestone 1 acceptance
of nonblank symbols longer than 20 characters; `Instrument.NormalizeSymbol`
reuses it and retains the 20-character registered-instrument constraint. A
focused HTTP regression test verifies the long-symbol success response.
Tightened the SQL integration fixture to require `127.0.0.1,1433` and reject
`localhost,1433` in the local Windows/Docker setup, where the port is bound
only on IPv4. Removed the duplicate README root-discovery description.

**Dependency investigation at that point:** source search found no
`System.Security.Cryptography.Xml` API usage, and the Infrastructure
dependency graph listed it as a direct top-level package rather than a
transitive EF Core or SqlClient requirement. The package was retained pending
approval to investigate its resolved-graph safeguard. The
`System.Security.Cryptography.Pkcs` security remediation was left untouched.

**Verification:** restore completed successfully, build completed with 0
warnings and 0 errors, and all 79 unit/in-memory HTTP tests passed. The full
solution run also passed the fixture endpoint guardrail test and skipped the
20 opt-in SQL tests because its connection variable was intentionally unset.
Two separate SQL integration runs then passed all 20 tests with 0 failures
and 0 skips each. Read-only checks found `FinCore`
unchanged at 0 portfolios, instruments, trades, and market prices;
`FinCoreIntegrationTests` remained at 3 portfolios, 6 instruments, 3 trades,
and 9 market prices; no run-owned database remained. The NuGet audit found
no vulnerable packages.

## Final dependency decision — cryptography version safeguard

**Decision:** retain the explicit `System.Security.Cryptography.Xml` 9.0.21
reference in `FinCore.Infrastructure`. The source does not directly use its
APIs, but removing the reference caused restore to select vulnerable
transitive versions: Xml 9.0.0 and
`System.Security.Cryptography.Pkcs` 9.0.4. Restore reported NU1902 and
NU1903 warnings. The original safe dependency configuration was restored
without changing package versions or suppressing warnings.

**Resolved versions:** Infrastructure resolves
`System.Security.Cryptography.Xml` 9.0.21 directly and
`System.Security.Cryptography.Pkcs` 9.0.21 transitively. The API continues to
reference `System.Security.Cryptography.Pkcs` 9.0.23 directly. The full
solution vulnerability audit reports no vulnerable packages.

**Verification:** `dotnet restore` succeeded. `dotnet build` succeeded with
0 warnings and 0 errors. The full connection-free solution run passed 79
unit/in-memory HTTP tests and the fixture endpoint guardrail test; 20 opt-in
SQL tests were skipped (0 failures). Two isolated SQL integration runs each
passed all 20 tests with 0 failures and 0 skips. Read-only pre- and post-run
checks matched: `FinCore` had 0 portfolios, instruments, trades, and market
prices; `FinCoreIntegrationTests` had 3 portfolios, 6 instruments, 3 trades,
and 9 market prices. No run-owned database remained, and `git diff --check`
passed.
