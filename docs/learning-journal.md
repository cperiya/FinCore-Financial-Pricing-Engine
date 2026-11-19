# Learning journal

## Milestone 3 - PR onboarding and reference corrections

**Decision and documentation:** documented the explicit, existing EF Core
migration for a fresh local `FinCore` development database, including tooling,
endpoint, password-handling, verification, and the absence of automatic API
migration. Existing databases require inspection and authorization before
an update. Expanded the architecture and class reference to cover the
benchmark harness, optional diagnostics, scalar default, opt-in JSON, and
the decision to defer Redis and parallel valuation.

**Verification and remaining work:** the Release build had zero warnings and
errors; 79 unit/HTTP tests, 23 SQL integration tests, and one database-free
integration fixture guardrail passed. Benchmark harness failure detection,
oracle and schedule self-tests passed; the transitive vulnerability audit was
clean. Protected database row counts were unchanged, and no benchmark- or
test-run databases remained. EF tooling discovered the checked-in migration
and generated its SQL offline. No development database migration or complete
fresh-clone walkthrough was performed. Redis and parallel valuation remain
unimplemented.

## Milestone 3 Phase 3.3A - counterbalanced benchmark correction

**Decision and implementation:** corrected the benchmark-only schedule so
each four-pair block includes both measurement orders for both portfolios.
Each scenario in a 100-pair run now records 25 pairs per portfolio/order.
Run, phase, portfolio, block, order, sample and per-pair measurements are
retained in the ignored traces; each scenario creates a fresh offline oracle.
The original order-confounded reports and documentation remain available.

**Results:** after focused scheduling/oracle checks and a six-pair pilot, three
independent owned-database matrices each used 10 warmups and 100 pairs per
scenario. At 50 instruments the corrected price-stage mean was
4.523-5.863 ms scalar versus 17.376-30.264 ms JSON; JSON was slower in
every portfolio/order subgroup. No plans were captured, so the cause is
unknown. At 2,000, means were 278.728-285.079 ms scalar and
49.429-54.486 ms JSON. Hypothetical reuse counts matched the original
results; they are not measured Redis hit rates or savings. See
[performance benchmarking](performance-benchmarking.md) for all results.

**Verification and remaining work:** Release build succeeded with zero
warnings/errors; scheduling and oracle checks, the pilot, 79 unit/HTTP and
24 SQL integration tests passed; the vulnerability audit was clean. Protected
row counts were unchanged and no benchmark-owned databases remained. Redis,
revision coordination, production behavior changes and new migrations remain
unimplemented; production Redis remains deferred.

## Milestone 3 Phase 3.3A - SQL-only cache feasibility

**Original measurement limitation:** portfolio A was always scalar-first
and portfolio B always JSON-first. The original measurements below are
preserved but are not a fully counterbalanced comparison.

**Implemented:** a benchmark-only paired SQL workload runner, actual selected
price-ID trace, and offline exact-cutoff and validity-interval reuse oracle.
The oracle invalidates affected instruments on every synthetic price write;
it is not a cache and does not model Redis latency. The existing price-stage
diagnostics measure repository execution and materialization. A focused
oracle self-test and a database-backed pilot precede the matrix.

**Measured:** three independent Release runs each used 10 warmups and 100
paired samples for four selected scenarios spanning 5 to 2,000 instruments.
At 2,000, price-stage means were 284.917-298.469 ms scalar and
53.224-53.589 ms JSON; the optimistic interval oracle observed 197,996 hits
per 200,000 instrument observations and 95 fully reusable batches per 100.
Changing cutoffs produced zero exact-key hits in the tested current-time
scenarios. At 50 instruments JSON was slower than scalar in all three runs;
no plans were captured, so the cause is unknown. Scenario factors are
confounded, and these synthetic reuse estimates are not Redis performance.
See [performance benchmarking](performance-benchmarking.md) for all results,
assumptions and limitations.

**Verification and decision:** the Release build had zero warnings/errors;
79 unit/HTTP and 24 SQL integration tests passed; the oracle and SQL pilot
passed; the NuGet vulnerability audit was clean. Owned benchmark databases
were removed and protected database row counts stayed unchanged. Existing
migrations were applied only to newly created owned benchmark/test databases
with explicit approval. Production Redis remains deferred: revision reads,
writer locking, invalidation, outage fallback and network costs are not
measured. No Redis, new migration, production query, financial-rule, API or
transaction change was implemented.

## Milestone 3 Phase 3.2D – final EF Core translation validation

**Implemented:** added a separate benchmark-only validation command and
workload without changing the Phase 3.1/3.2C datasets or production query
default. The new synthetic cases cover 1, 5, 50, 500, 2,000, and 2,001
instruments, 12 trades per instrument, sparse and backdated prices, future
prices, and same-time price-ID ties. The runner verifies exact valuation
results and executed ID batches, captures actual plans, and measures matched
scalar/JSON BenchmarkDotNet allocation and end-to-end results. Integration
tests now compare both modes for missing prices, decimal accuracy, overflow,
cancellation, and concurrent atomic trade/price writes.

**Measured findings:** with the richer 12-trade workload, BDN allocations at
2,000 instruments were 584,587.20 KB scalar and 17,587.48 KB JSON; at 2,001
they were 584,656.35 KB and 17,635.50 KB. Paired end-to-end p50 was
849.555/250.749 ms at 2,000 and 893.760/260.279 ms at 2,001 (scalar/JSON).
Small-workload BDN latency was mixed, while end-to-end p50s were similar at
1, 5, and 50 instruments. The 2,001 case retained two 2,000-bounded batches
per repository and six reader commands in both modes.

**Plan and design decisions:** 28 actual plans were captured only on the
run-owned database at compatibility level 160. Scalar plans used scans and
sort/hash operators at 500 and 2,000 IDs; JSON plans used nested loops and
index seeks across the tested sizes. `OPENJSON` estimated 50 input rows for
all collection sizes, including 2,000 actual IDs; no captured plan had
warnings or spills. Retain the 2,000-ID batch size conservatively. Keep JSON
opt-in: the large-workload gains are clear on this local synthetic workload,
but small-case results are mixed, cardinality estimates remain a risk, and
one local run is insufficient to promote the production default.

**Verification:** Release solution build succeeded with zero warnings and
errors; all 79 unit/HTTP tests and all 24 SQL integration tests passed. The
transitive NuGet vulnerability audit found no vulnerable packages.
Compatibility level was 160; the validation-owned database was cleaned,
protected snapshots remained `FinCore` 0/0/0/0 and
`FinCoreIntegrationTests` 3/6/3/9, no benchmark-owned database remained, and
`git diff --check` passed. Reports and actual plans remain in the ignored
`BenchmarkDotNet.Artifacts/TranslationValidation/` directory.

**Not implemented:** production-default promotion, query or financial-rule
changes, schema/index/migration changes, Redis, cache behavior, parallel
valuation, package changes, commit, or push.

## Milestone 3 Phase 3.2C – controlled EF Core collection translation experiment

**Implemented:** added a default-off Infrastructure option to compare
EF Core 10's existing scalar collection translation with query-local
`EF.Parameter(batch).Contains(...)` for only the instrument and latest
market-price ID predicates. Added a paired BenchmarkDotNet mode that keeps
one ownership-verified database alive across worker processes, verifies
generated SQL and expected valuation output, captures actual plans, and
separately measures end-to-end percentiles and SQL reader durations. No
package, schema, migration, trade behavior, Domain rule, API contract, Redis,
or parallel EF query changed.

**Measured findings:** the run-owned database used SQL Server 2022 Developer
compatibility level 160 and was shared for both variants. SQL contained
`N` scalar ID parameters for instruments and `N + 1` for prices in the
default path; JSON mode used one ID JSON parameter and, for prices, one
additional UTC cutoff parameter. EF's price query references that JSON
parameter from two `OPENJSON` row sources after query translation.
Historical cutoff, future-price exclusion, deterministic timestamp/ID
tie-breaking, missing-price failure, and all synthetic totals remained
correct.

The nine-observation BDN median fell from 87.192 to 43.590 ms at 500
instruments and from 783.105 to 185.066 ms at 2,000. Per-operation
allocations fell from 38,230.95 to 1,931.11 KB and from 574,418.46 to
7,386.88 KB respectively. The 100-sample paired end-to-end p50 fell from
74.056 to 35.116 ms at 500 and from 588.181 to 101.355 ms at 2,000. At
5 instruments the p50 was effectively unchanged. The two variants retained
four reader commands per valuation.

**Plan and design decisions:** at 500 and 2,000 instruments, captured scalar
plans used clustered/index scans for these batch reads; JSON plans used
clustered and market-price index seeks with no captured spills or warnings.
The estimated plan cost was lower for JSON in both queries and workloads.
However, SQL Server estimated each `OPENJSON` collection at 50 rows even
when the actual input was 2,000. Keep the query option disabled by default
and treat larger or production-like workloads as required follow-up before
any promotion. BenchmarkDotNet used three launches and three measured
iterations per launch; the 2,000 case remains noisy. Stage allocation deltas
are approximate and are not substituted for `MemoryDiagnoser`.

**Verification:** Release solution build succeeded with zero warnings and
errors; all 79 unit/HTTP tests and all 22 SQL integration tests passed.
The complete transitive NuGet audit found no vulnerable packages.
Compatibility level was 160; the exact run-owned benchmark database was
dropped after the worker lifecycle, pre-existing owned databases were
preserved, and protected database snapshots remained unchanged
(`FinCore` 0/0/0/0; `FinCoreIntegrationTests` 3/6/3/9). `git diff --check`
passed. Results remain in the ignored
`BenchmarkDotNet.Artifacts/TranslationExperiment/` directory.

**Not implemented:** Redis, caching, migrations, schema/index changes,
permanent activation of JSON translation, business-logic optimization,
parallel valuation, or commit/push.

## Milestone 3 Phase 3.2B – targeted allocation profiling

**Implemented:** added a benchmark-only `--allocation-profile` mode that
waits until its isolated run-owned SQL Server database is migrated, seeded,
and correctness-checked before profiler attachment. It records paired
diagnostics-off and diagnostics-on service-call windows for 50, 500, and
2,000 instruments, after five warmups and with ten measured calls per mode.
Each trace is bounded to 45 seconds and a 64 MB buffer. The final measured
loop invokes the valuation service through a fresh DI scope and excludes the
repeated output-validation pass; the instrumented mode resets its collector
before each service call. Local traces and analysis reports remain
Git-ignored for review.

**Measured findings:** the installed `dotnet-trace` version was 10.0.750501.
All three final traces had allocation stacks, zero lost events, and no
allocation ticks without stacks. Sample-weighted estimated allocation per
call was about 0.865 MB at 50 instruments, 39.198 MB at 500, and 588.194 MB
at 2,000; the 50-instrument sample had only 81 allocation ticks and is sparse.
At 500, `System.Char[]`, `System.Text.StringBuilder`, and `System.String`
accounted for about 93.6% of sampled bytes; at 2,000, about 98.3%.

**Root cause evidence:** 50/500 allocation stacks pass through EF Core
`Uniquifier.Truncate` and `Uniquify`; at 2,000, the dominant stacks include
`SqlNullabilityProcessor.ProcessInExpressionValues` and `VisitIn`. Both the
instrument and latest-market-price repository queries pass up to 2,000 IDs
through `batch.Contains(...)`, which requires per-execution SQL `IN` parameter
expansion and name uniquification. This identifies the main allocation
source, while the whole-service trace cannot exactly divide allocations
between the two queries. The prior 3.2A stage deltas of roughly 285 MB each
for instrument and market-price retrieval are consistent with that evidence.
The Phase 3.1 allocation baseline grows about 45.5x for a 10x increase in IDs
(50 to 500) and 15.0x for a 4x increase (500 to 2,000). This is measured
superlinear growth, but the samples do not establish EF's asymptotic
complexity.

The 2,000-instrument trace observed 40 large-kind allocation ticks weighted
at about 7.02 MB over ten calls and approximately 1,400 Gen0 GC start events.
These are sampling/event counts, not exact LOH bytes, retained heap, or a GC
pause-time measurement. The diagnostics-on/off estimated allocation
difference was under 0.4% at 500 and 2,000, but the paired trace order and
short sample count do not establish instrumentation latency overhead.
The 500-instrument BDN mean versus end-to-end p50 discrepancy remains
unresolved; profiler timings are not comparable because they run under
GC tracing.

**Engineering decisions:** preserve the SQL-only baselines and all trace
artifacts, and make no production query, financial-rule, transaction,
schema, API, Redis, or Docker changes. Future experiments should first
benchmark smaller ID batches, then compare a server-side set input such as a
table-valued or supported JSON parameter with matched correctness, query-plan,
allocation, and latency measurements. A table-valued parameter would require
a separate database-type deployment decision. EF operations remain
sequential on the scoped context.

**Verification:** Release solution build succeeded with 0 warnings and 0
errors. The unit/in-memory HTTP suite passed 79 tests and the SQL integration
suite passed 20 tests. The transitive NuGet vulnerability audit reported no
vulnerable packages. No run-owned benchmark database remained; protected
database snapshots were unchanged (`FinCore` 0/0/0/0 and
`FinCoreIntegrationTests` 3/6/3/9). The generated profiler files are under
the ignored `BenchmarkDotNet.Artifacts/AllocationTraces/` directory.

**Not implemented:** allocation or query optimization, Redis, parallel
valuation, migrations, schema/index changes, or a matched BDN/end-to-end
timing experiment.

## Milestone 3 Phase 3.2A – SQL valuation diagnostics

**Implemented:** added an opt-in Application diagnostics sink used only by
the benchmark project. It records actual valuation stage timings, logical
input/output counts, approximate managed allocation deltas, and correlated
reader command metrics without changing valuation rules, SQL queries,
transaction isolation, migrations, or API contracts. Added isolated actual
SQL execution-plan capture with `SET STATISTICS XML ON`, plus a separate
diagnostic report directory.

**Measured findings:** with 100 measured calls and 10 warmups, the 2,000
instrument valuation averaged 637.665 ms in the diagnostic run. Trade
retrieval averaged 26.266 ms, instrument retrieval/materialization 293.156
ms, market-price retrieval/materialization 313.484 ms, position
calculation 1.711 ms, and response construction plus checked summation
0.546 ms. The two EF/SQL materialization stages accounted for the dominant
approximate allocation deltas: about 285,293 KB for market prices and
285,192 KB for instruments, against about 574,744 KB for the complete
valuation. The workload returned 4,000 trades, 2,000 instruments, and 2,000
eligible prices through four sequential reader commands.

**SQL plan evidence:** the captured trade valuation plan used a clustered
scan on `PK_Trades` followed by a sort; the existing
`IX_Trades_Portfolio_Instrument_Execution` was not selected. The market
price plan used the latest-price index with grouping/top-selection and sort
operators. The instrument lookup used primary-key access with parameter
joining and sorting. These findings are evidence for a future index/query
investigation, not an approved optimization.

**Diagnostic limitations:** allocation values are approximate process-wide
deltas around asynchronous stage boundaries and are not authoritative
ownership. BenchmarkDotNet remains authoritative for allocation-per-operation
totals. Transaction begin/commit/disposal protocol work is not separately
observable through the reader interceptor. The 500-instrument BDN mean versus
end-to-end p50 difference remains unresolved because the runs differ in
worker process, database population, warmup, plan-cache state, and summary
statistic. No performance optimization, Redis integration, parallel
valuation, migration, or schema change was implemented.

**What to learn next:** use allocation-stack profiling or an equivalent
controlled profiler on the isolated benchmark process to distinguish
SqlClient/EF buffering from materialization and runtime allocations. Compare
the captured trade plan with candidate indexes only in a future approved
optimization phase, then repeat BDN and end-to-end measurements against
matched database state.

**Validation commands:**

```powershell
dotnet build .\FinCore.slnx --configuration Release
dotnet test .\FinCore.slnx
dotnet list .\FinCore.slnx package --vulnerable --include-transitive
git diff --check
```

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

## Milestone 3 Phase 3.1 – SQL-only valuation performance baseline

**Implemented:** added the separate `FinCore.Benchmarks` .NET 10 console
project with BenchmarkDotNet 0.15.8. The benchmark references Application and
Infrastructure without changing production valuation logic. Its deterministic
5-, 50-, 500-, and 2,000-instrument workloads use fixed synthetic IDs, 100
share BUYs, 20 share SELLs, older prices, eligible prices, and future-dated
prices. Every workload verifies the expected 80-share holdings, inclusive
historical price cutoff, selected timestamps, and total before measurement.
The 2,000-instrument case exercises the existing 2,000-ID price-query batch
boundary.

**Safety and measurement decisions:** each run creates only a unique
`FinCoreBenchmarkRun_<GUID>` database after validating the encrypted
`127.0.0.1,1433` SQL Server 2022 Developer instance. It applies the existing
migration, records a run marker, holds master and database SQL application-lock
leases for the worker lifetime, and drops only the exact verified database.
Interrupted runs retain an ignored manifest and require explicit
name-specific cleanup. Protected `FinCore` and `FinCoreIntegrationTests`
databases are never benchmark targets. Provisioning, migration, and seeding
are outside timed operations. Valuation calls use fresh scopes and contexts,
while EF reads remain sequential in the existing serializable transaction.

**Measured baseline:** the Release repeated run used 10 warm-ups and 100
samples per workload. Results were p50/p95/p99 in milliseconds and sequential
calls per second: 5 instruments: 690.651 ms first call, 13.035/15.793/17.589
ms, 76.74 calls/s; 50: 61.136 ms, 24.535/29.094/35.425 ms, 40.44 calls/s;
500: 283.100 ms, 63.983/78.864/81.983 ms, 14.89 calls/s; 2,000: 1,988.790
ms, 570.130/602.936/651.101 ms, 1.74 calls/s. BenchmarkDotNet ShortRun
reported mean/median/stddev/allocations of 12.931/12.437/1.270 ms/226.04 KB,
20.170/20.184/0.453 ms/839.68 KB, 118.187/121.547/7.359 ms/38,227.99 KB,
and 627.316/626.140/3.241 ms/574,419.16 KB respectively. The separate
diagnostics run observed 4 reader commands per valuation and SQL reader
execution averages of 8.424, 17.506, 31.120, and 197.985 ms for those sizes.

**Limitations:** a 1,000-sample attempt timed out in the existing SQL command
during the 2,000-instrument workload and was not reported as a result. The
completed 100-sample run is the accepted local baseline. It measures
sequential service calls, not HTTP, Redis, parallelism, or production
capacity. The large allocation and latency growth at 500/2,000 instruments
are bottleneck signals for later query/materialization investigation, not
evidence that caching or parallel valuation will improve performance.

**Verification:** restore and Release build completed with 0 warnings and
0 errors. The unit/in-memory HTTP suite passed 79 tests. The isolated SQL
suite passed 20 tests with 0 failures and 0 skips. The full transitive NuGet
audit reported no vulnerable packages; BenchmarkDotNet resolved to 0.15.8.
Protected database snapshots remained FinCore 0/0/0/0 and
FinCoreIntegrationTests 3/6/3/9. Benchmark-owned databases were safely
cleaned, and `git diff --check` passed.

## Milestone 3 benchmark harness reliability

The paired translation runners now validate the complete workload/mode
matrix, each report and each worker launch before writing a successful run
report. A BenchmarkDotNet summary can contain failed reports, and a worker
may return a nonzero exit code even after producing measurements, so neither
summary presence nor measurements alone are sufficient. Partial BenchmarkDotNet
exports remain local and are marked incomplete or failed rather than deleted.

A database-free simulation exercises real successful and failing workers,
missing and duplicate cases, missing summaries or launches, a nonzero exit
with measurements, and missing allocation data. An additional intentionally
failing worker verified cleanup of an ownership-checked benchmark database.
The existing run-owned fixture remains responsible for verified cleanup.
This change does not promote JSON translation, add caching or parallel
valuation, or alter the earlier benchmark results.

Verification used the existing Release build, 79 unit/HTTP tests and 24 SQL
integration tests. A separate validation run completed all 12 BenchmarkDotNet
cases and 36 worker launches; the final finite-statistics guard was added
after that full run and exercised in the successful database-free simulation.
The SQL failure simulation verified owned-database cleanup. Protected database
row-count snapshots were unchanged, no benchmark-owned database remained,
and the transitive vulnerability and whitespace checks passed.

## Milestone 3 baseline runner reliability

The default SQL-only entry point previously ignored BenchmarkDotNet summaries,
so a failed case could leave the process reporting success. It now derives
the expected cases from the parsed configuration and validates reports,
worker exits, launch counts when explicitly configured, and required timing
and allocation data. Failed or interrupted runs retain local status markers.
Automatic launch counts remain controlled by BenchmarkDotNet.

Baseline setup now attempts fixture cleanup when setup fails and reports both
setup and cleanup exceptions if necessary. Cleanup still refuses a database
without verified ownership; interrupted processes retain manifest-based
recovery. Focused simulations cover the default process exit codes, failed
setup after database initialization, and ownership refusals. A separate
four-case SQL-backed dry run verified the default entry point; it is a
functional smoke test, not a replacement performance baseline. The README
connection-string example is explicitly local and obtains its password from
the ignored environment file rather than embedding a credential.
