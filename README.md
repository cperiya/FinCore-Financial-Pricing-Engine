# FinCore – Financial Pricing & Portfolio Risk Engine

FinCore is a .NET 10 modular-monolith starter for a financial pricing and
portfolio risk engine. Its HTTP API values stock positions and supports USD
portfolio, equity instrument, trade, derived-position, and simulated market
price workflows. Portfolio persistence uses EF Core 10 and SQL Server 2022;
trade writes use transaction-owned SQL Server locks. A fresh local `FinCore`
development database requires the checked-in migration. FinCore does **not** load
live market data, convert between currencies, or persist derived positions.

> **Development-only security warning:** This API currently has no
> authentication or authorization. It is intended for trusted local
> development and educational use only. Do not expose it to the public
> internet or untrusted networks.

## Implemented: stock position valuation

The API calculates:

```text
MarketValue = UnitPrice * Quantity
```

Both inputs use `decimal`. Unit price and quantity must be greater than zero;
negative and zero values are rejected. Fractional share quantities are
supported. A non-empty symbol is required and normalized to uppercase; the
legacy stock-pricing endpoint does not apply the 20-character registered-
instrument storage limit. If the product exceeds the range of `decimal`, the
API returns a validation error.

Currency is caller-known: `UnitPrice` and `MarketValue` are assumed to use the
same currency. The request and response do not carry a currency code, and no
foreign-exchange conversion is performed. Callers must not combine values
denominated in different currencies.

### Try the endpoint

Start the API from the repository root:

```powershell
dotnet run --project .\src\FinCore.Api\FinCore.Api.csproj
```

The HTTP launch profile listens at `http://localhost:5157`. Example request:

```http
GET http://localhost:5157/api/pricing/stock?symbol=msft&unitPrice=12.50&quantity=4
```

Successful response (`200 OK`):

```json
{
  "symbol": "MSFT",
  "unitPrice": 12.50,
  "quantity": 4,
  "marketValue": 50.00
}
```

Missing or blank symbols, missing or malformed numeric values, and zero or
negative numeric values produce `400 Bad Request` with validation details.
The OpenAPI document is available in Development at
`http://localhost:5157/openapi/v1.json`.

The root URL `http://localhost:5157/` returns a JSON discovery document with
the application name, description, and a templated stock valuation link. The
OpenAPI link is included only in Development, where the OpenAPI endpoint is
mapped.

The stock valuation request flow is: HTTP query binding and validation in
`FinCore.Api` → symbol normalization and use case in
`FinCore.Application` → arithmetic and positive-value invariants in
`FinCore.Domain` → application result → HTTP response. See the
[API documentation](docs/api-documentation.md) and
[class and method reference](docs/class-method-reference.md) for the full
contract and source walkthrough.

## Portfolio Application workflows

The API and Application layers can create, retrieve, and list USD portfolios;
register USD equity instruments with trimmed, uppercase unique symbols;
record BUY and SELL trades; derive positions from trade history; and record or
retrieve simulated USD market prices. See the [REST API reference](docs/api-documentation.md)
for all endpoint contracts and examples.

Trade recording validates the complete chronological position history,
including backdated candidates. Ordering is execution time, BUY before SELL,
then trade ID; any intermediate negative holding rejects the write. A
caller-supplied trade GUID is the idempotency key and the persisted trade
primary key. An identical retry returns the existing trade; using that ID for
a different payload produces an explicit conflict.

Each BUY and SELL runs in a SQL Server `SERIALIZABLE` transaction and acquires
transaction-owned exclusive `sp_getapplock` locks for the trade ID and
portfolio/instrument position in ordinal resource-name order. A competing
trade for the same position waits, then validates against the committed
history. Lock timeouts, deadlocks, command timeouts, and recognized severe SQL
TCP connection timeouts fail explicitly and roll back; only the recognized
transient errors map to a sanitized `503`, while authentication and SQL
configuration errors are not classified as retryable. After an uncertain
trade outcome, retry the same payload with the same trade ID. Market-price
lookup requires an explicit UTC cutoff and returns only a price with
`AsOfUtc <= cutoff`, or `null` when no eligible price exists.

### Portfolio and trading REST API

The API exposes:

- `POST /api/portfolios`, `GET /api/portfolios`,
  `GET /api/portfolios/{id}`, and `GET /api/portfolios/{id}/positions`.
- `POST /api/instruments`, `POST /api/portfolios/{id}/trades`,
  `POST /api/instruments/{id}/prices`, and
  `GET /api/instruments/{id}/prices/latest?asOfUtc=...`.
- `GET /api/portfolios/{id}/valuation?asOfUtc=...` values every open position
  at one UTC cutoff. If omitted, the Application service captures
  `TimeProvider.GetUtcNow()` once for the request.

Request DTOs enforce USD-only currency, string-valued trade sides, positive
`decimal(28,10)` values, and zero-offset UTC timestamps. The controllers
delegate trade and position decisions to the existing Application and Domain
logic. Portfolio creation returns `201 Created` with a `Location`; the other
POST operations return `200 OK`, including exact trade replays, because this
minimal surface has no exact-resource GET endpoints for them. A caller-supplied
`tradeId` is the retry key. Exact retries return the stored trade, while
conflicting payloads and oversells return `409`. Known lock timeouts and
deadlocks return `503` with a retry hint; retry the identical payload with the
same trade ID. All errors use Problem Details without database details.

Portfolio valuation includes only trades and market prices at or before the
effective cutoff. It reuses the Domain position calculator and
`StockValuationService`, returns each open position's symbol, quantity,
selected USD price and timestamp, unrounded market value, and the USD total.
An empty or fully closed portfolio returns an empty positions list and a zero
total. If any open position lacks an eligible price, the whole request returns
`409` with all affected instrument IDs and symbols; no partial total is
returned. Old prices are not rejected as stale, but their observation times
are included. Future-dated prices are excluded.

The valuation reads execute sequentially, without tracking, in a read-only
SQL Server `SERIALIZABLE` transaction on the scoped `FinCoreDbContext`.
Serializable range locks can block concurrent trade or price writes and may
cause deadlocks; known deadlocks are rolled back and reported as retryable
`503` responses. For example, a 100-share MSFT buy, a 20-share sale, and an
eligible simulated USD 450 price produce a remaining quantity of 80 and a
market value of USD 36,000.

## Projects and dependency direction

| Project | Responsibility |
| --- | --- |
| `FinCore.Api` | HTTP endpoints, request validation, response mapping, and application composition. |
| `FinCore.Application` | Stock-pricing, portfolio-management, and portfolio-valuation use cases, repository contracts, and unit-of-work transaction contract. |
| `FinCore.Domain` | Stock market-value calculation, USD portfolio entities, and long-only position rules. |
| `FinCore.Infrastructure` | EF Core 10 SQL Server context, mappings, repositories, migrations, and dependency-injection registration. |
| `FinCore.UnitTests` | xUnit Domain and Application tests plus in-memory API integration tests. |
| `FinCore.IntegrationTests` | Opt-in SQL Server persistence, transaction, and HTTP tests using a run-owned test database. |
| `FinCore.Benchmarks` | Separate SQL-only portfolio valuation benchmarks using BenchmarkDotNet and a benchmark-owned database. |

Dependencies point inward: API → Application → Domain; API → Infrastructure
for adapter composition; Infrastructure → Application and Domain.
Application and Domain do not reference EF Core. UnitTests references the API,
Application, and Domain; IntegrationTests references Infrastructure and
the API and exercises the SQL Server adapters through HTTP.

### Cryptography package version safeguard

`FinCore.Infrastructure` explicitly references
`System.Security.Cryptography.Xml` 9.0.21 as a dependency-version safeguard;
Infrastructure source does not directly use its APIs. Removing this
reference caused restore to select vulnerable transitive versions of
`System.Security.Cryptography.Xml` 9.0.0 and
`System.Security.Cryptography.Pkcs` 9.0.4, producing NU1902/NU1903 warnings.
Keep the explicit safeguard in place unless the underlying dependency graph
can be changed without restoring those vulnerable versions. The current
Infrastructure graph resolves Xml 9.0.21 and Pkcs 9.0.21. The API separately
pins `System.Security.Cryptography.Pkcs` 9.0.23; that remediation remains
unchanged.

## Local SQL Server 2022 development environment

FinCore provides a local SQL Server 2022 Developer Edition container for
learning and development only; it is not production configuration. The API
can connect when `ConnectionStrings__FinCore` is provided. Apply the existing
migration explicitly to a fresh development database as described below; the
API does not migrate at startup. The Domain project remains independent of EF
Core and SQL Server.

### Prerequisites

- .NET 10 SDK (`dotnet --version`).
- Docker Desktop with Compose and Linux container support; the supplied
  Compose file runs SQL Server 2022 Developer Edition.
- EF Core command-line tooling compatible with the project's EF Core 10.0.0
  packages. For example, install `dotnet-ef` 10.0.0 if it is not already
  installed:

  ```powershell
  dotnet tool install --global dotnet-ef --version 10.0.0
  dotnet ef --version
  ```

  If a global version is already installed, update it to 10.0.0 instead with
  `dotnet tool update --global dotnet-ef --version 10.0.0`. Ensure the .NET
  global tools directory is on `PATH`.

### Docker Desktop on Windows

Use Docker Desktop with the WSL 2 engine, Linux containers, and WSL integration
enabled for the working distribution. Run the following commands from the
repository root in PowerShell:

```powershell
Copy-Item .\.env.example .\.env
notepad .\.env
```

Replace the placeholder password in `.env` with a unique, strong local
password before starting SQL Server. The SQL Server image requires at least
eight characters and characters from at least three of these groups:
uppercase, lowercase, numeric, and special characters. Keep `.env` local; Git
ignores it. For the unquoted `.env` and connection-string examples below, use
letters, digits, and special characters such as `!`, `_`, or `-`; avoid
whitespace, quotes, semicolons, `$`, and `#`, which need additional escaping.

```powershell
docker compose config --quiet
docker compose up -d
docker compose ps
docker compose logs --tail 100 sqlserver
```

Wait for SQL Server's ready-for-client-connections message in the logs. To
verify SQL authentication without putting the password in the command line,
open the SQL prompt inside the container. `sqlcmd` prompts for the password;
enter it without echoing it, then run a test query:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C
```

At the `sqlcmd` prompt:

```text
SELECT DB_NAME();
GO
```

The query should return `master`. If the `sqlcmd` path is unavailable in the
image, connect with a local SQL client as described below.

### Compose configuration

| Property | Purpose |
| --- | --- |
| `services` / `sqlserver` | Defines the SQL Server service and its Compose network name. |
| `image` | Uses Microsoft's official SQL Server 2022 Linux image. |
| `environment.ACCEPT_EULA` | Required by the image to accept the license terms. |
| `environment.MSSQL_PID` | Selects Developer Edition for local learning and development. |
| `environment.MSSQL_SA_PASSWORD` | Supplies the SA password through Compose interpolation from the untracked `.env`; startup fails if it is missing. |
| `ports` | Publishes container port 1433 only on host loopback (`127.0.0.1`), not on external network interfaces. |
| `volumes` service mapping | Mounts the named volume at `/var/opt/mssql`, where SQL Server stores database files. |
| `restart` | Restarts the container after failure or Docker Desktop restart unless it was explicitly stopped. |
| Top-level `volumes.sqlserver_data` | Declares persistent Docker-managed storage that survives container removal and recreation. |

### Connecting and stopping

From Windows, configure a SQL client such as SQL Server Management Studio,
Azure Data Studio, or the VS Code SQL extension with:

- Server: `127.0.0.1,1433`
- Authentication: SQL Server Authentication
- Login: `sa`
- Password: the local value in `.env` (do not paste it into source files or command history)
- Initial database: `master`
- Encryption: enabled; trust the server certificate for this local development instance

Another container on this Compose network should connect using host
`sqlserver` and port `1433`. No application container or connection string is
stored in the repository. The API reads the optional
`ConnectionStrings:FinCore` setting from configuration (for example the
`ConnectionStrings__FinCore` environment variable); ASP.NET Core maps the
double underscore to the configuration separator. The value must be provided
out of band and must not be committed or printed.

Stop the service while preserving its database volume:

```powershell
docker compose down
```

`docker compose down --volumes` permanently deletes the named database volume.
Do not use it unless you intentionally want to erase local database data.

### Troubleshooting

- If Docker cannot start a Linux image, enable the WSL 2 engine, Linux
  containers, and the repository distribution's WSL integration in Docker
  Desktop.
- If port 1433 is already allocated, stop the other local SQL Server process
  before starting this service; the configured binding intentionally remains
  loopback-only on port 1433.
- If startup fails password validation, set a password meeting SQL Server's
  complexity requirements in `.env`, then retry and inspect
  `docker compose logs --tail 100 sqlserver`.
- Changing `.env` after the named volume has been initialized does not change
  the SA password stored by SQL Server. Use the existing password to rotate
  it, or intentionally discard the local data volume only if its data is no
  longer needed.
- SQL Server may take some time to become ready after the container starts.
  Check its logs rather than relying only on `docker compose ps`.

## Initialize the local development database

The Application repository interfaces and Infrastructure EF Core 10
implementation are present. The generated migration
`20261115200739_InitialPortfolioPersistence` in
[`Persistence/Migrations`](src/FinCore.Infrastructure/Persistence/Migrations/)
creates `Portfolios`,
`Instruments`, `Trades`, and `MarketPrices` with the configured constraints,
foreign keys, and indexes. A previous local development database was verified
with this migration; a fresh clone and fresh Docker volume do not contain it.

The previously applied schema was verified against SQL Server: portfolio and instrument
names are `nvarchar(200)`, symbols are `nvarchar(20)` with a unique index,
financial values are `decimal(28,10)`, and timestamps are
`datetimeoffset(7)`. The trade-history and latest-price indexes match the
repository query ordering documented in the
[database design](docs/database-design.md).

After starting the local SQL Server service and waiting for it to be ready,
run the following from the repository root in PowerShell. This is for a **fresh
local development database only**. EF may create `FinCore` if it is absent.
If `FinCore` already contains data or later migrations, inspect its state and
obtain authorization before running an update: specifying an older target can
otherwise reverse later migrations. Never use these instructions for
`FinCoreIntegrationTests` or another database.

The design-time `FinCoreDbContextFactory` reads `ConnectionStrings__FinCore`.
The fixed server and catalog below select only `127.0.0.1,1433` and `FinCore`;
verify that this endpoint is your intended local development SQL Server.
Do not pass a connection string or password as a command-line argument.

```powershell
$passwordLine = Get-Content .\.env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1
if (-not $passwordLine) { throw 'MSSQL_SA_PASSWORD is missing from .env.' }
$saPassword = $passwordLine.Substring('MSSQL_SA_PASSWORD='.Length)
$env:ConnectionStrings__FinCore = "Server=127.0.0.1,1433;Database=FinCore;User Id=sa;Password=$saPassword;Encrypt=True;TrustServerCertificate=True"

dotnet ef database update 20261115200739_InitialPortfolioPersistence --project .\src\FinCore.Infrastructure\FinCore.Infrastructure.csproj --startup-project .\src\FinCore.Api\FinCore.Api.csproj --context FinCoreDbContext
if ($LASTEXITCODE -ne 0) { throw 'Development database migration failed.' }
```

To verify the migration without placing the password in the command line,
connect to the local database with the password prompt:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -d FinCore
```

At the SQL prompt, run:

```sql
SELECT DB_NAME() AS DatabaseName;
SELECT MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId;
GO
```

The database name should be `FinCore` and the history should include
`20261115200739_InitialPortfolioPersistence`. If the image lacks that
`sqlcmd` path, use a local SQL client with the connection settings above.

The API registers persistence only when `ConnectionStrings__FinCore` is set.
Portfolio Application services are registered alongside the repositories and
unit of work under the same condition; the existing stock endpoints remain
available without a database connection. The API does **not** apply migrations
on startup. In the same PowerShell session, start the API after verification:

```powershell
dotnet run --project .\src\FinCore.Api\FinCore.Api.csproj
```

After stopping the API, clear the session-only connection and password:

```powershell
Remove-Item Env:\ConnectionStrings__FinCore -ErrorAction SilentlyContinue
$saPassword = $null
$passwordLine = $null
```

The connection uses SQL authentication, encryption, and trusts the
self-signed certificate only for this local development instance. There is no
credential in `appsettings.json` or source code. Do not copy the password into
a command literal, logs, or repository files.

### SQL Server integration tests

The SQL Server integration tests are opt-in. Set the test connection variable
as shown below; its `FinCoreIntegrationTests` catalog is used only as the
credential/configuration source and is never opened by the fixture. The
fixture validates that the endpoint is local loopback port 1433 and that the
connected server is SQL Server 2022 Developer Edition with the required
administrative permission. It then uses `master` to create a fresh
`FinCoreTestRun_<GUID>` database, applies the existing migration with
`MigrateAsync`, and directs test contexts only to that generated database.

The fixture records an ownership marker containing the run ID. Cleanup
revalidates the generated name, local server, and exact ownership marker
before dropping only that run's database. If any ownership check fails, it
refuses to drop the database and reports the failure. A new fixture run uses
a new GUID, so simultaneous runs do not share databases; SQL tests within a
run are serialized. Test rows are isolated in the temporary database and do
not accumulate between runs. Neither `FinCore` nor the existing
`FinCoreIntegrationTests` database is modified, reset, or dropped.

On Windows, keep the test connection's server address as
`127.0.0.1,1433`, not `localhost,1433`. The Compose port is published only on
IPv4, while Windows resolves `localhost` to IPv6 `::1` before IPv4
`127.0.0.1`. Microsoft.Data.SqlClient can wait for its connection timeout on
the unavailable IPv6 endpoint before reaching SQL Server. Using the explicit
IPv4 address avoids that TCP pre-login timeout; encryption and certificate
settings remain enabled. The integration fixture rejects `localhost,1433`
and requires the explicit IPv4 endpoint for this documented local setup.

The fixture applies migrations rather than calling `EnsureCreatedAsync`.
`EnsureCreatedAsync` does not apply migration history and does not update an
existing schema, so it is unsuitable for verifying migration behavior.

```powershell
$passwordLine = Get-Content .\.env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' }
$saPassword = $passwordLine.Substring('MSSQL_SA_PASSWORD='.Length)
$env:FINCORE_SQLSERVER_TEST_CONNECTION_STRING = "Server=127.0.0.1,1433;Database=FinCoreIntegrationTests;User Id=sa;Password=$saPassword;Encrypt=True;TrustServerCertificate=True"
dotnet test .\tests\FinCore.IntegrationTests\FinCore.IntegrationTests.csproj --filter "Category=Integration"
Remove-Item Env:\FINCORE_SQLSERVER_TEST_CONNECTION_STRING -ErrorAction SilentlyContinue
$saPassword = $null
```

Without that environment variable, these tests are skipped. The normal
solution test run still executes the Domain and API tests. Migration tests
verify `__EFMigrationsHistory`, foreign keys and restricted delete rules,
check constraints, index key order, the unique symbol index, and rejection
of duplicate symbols after Domain normalization.
`PortfolioApiIntegrationTests` starts the API with the fixture connection and
asserts that the resolved `FinCoreDbContext` database name is exactly the
fixture's generated `FinCoreTestRun_<GUID>` before making HTTP requests.

## SQL-only portfolio valuation benchmarks

The separate `FinCore.Benchmarks` project measures the existing
`PortfolioValuationApplicationService` against SQL Server. It uses
BenchmarkDotNet 0.15.8 for mean, median, standard deviation, and
`MemoryDiagnoser` allocation results, plus a repeated measurement mode for
warm p50/p95/p99 latency and sequential throughput. The benchmark workloads
contain 5, 50, 500, and 2,000 synthetic instruments. Each instrument has a
100-share BUY, a 20-share SELL, an older price, one eligible price, and a
future-dated price. Before results are recorded, the harness verifies
80-share holdings, the eligible price timestamp and amount, and the expected
portfolio total at the fixed UTC cutoff.

Benchmarks require the local SQL Server 2022 Developer instance at
`127.0.0.1,1433`. The process-scoped connection must target `master`, use
encrypted SQL authentication, trust the local development certificate, and
enable pooling. The fixture creates only a unique
`FinCoreBenchmarkRun_<GUID>` database, applies the existing migration, records
an ownership marker, and holds SQL application-lock leases for the worker
lifecycle. Provisioning, migration, and seeding are not timed. Normal
completion drops only the exact marked database; interrupted runs retain a
manifest and are never cleaned by a broad name/prefix sweep. Generated
BenchmarkDotNet output and manifests are ignored by Git.

The paired translation runners validate every expected BenchmarkDotNet case
and worker launch before reporting success. Partial artifacts are marked
`INCOMPLETE` or `FAILED`. The database-free harness failure simulation is
documented in the [performance guide](docs/performance-benchmarking.md).
The default SQL-only BenchmarkDotNet entry point also checks its expected
cases and returns a nonzero exit code for invalid or failed runs.

The following is a local-development connection-string template, not a stored
credential. Load the password from the ignored `.env` into this PowerShell
process without printing it, then run the desired mode. These commands do not
start Docker or modify either protected FinCore database:

```powershell
$passwordLine = Get-Content .\.env | Where-Object { $_ -match '^MSSQL_SA_PASSWORD=' } | Select-Object -First 1
if (-not $passwordLine) { throw 'MSSQL_SA_PASSWORD is missing from .env.' }
$saPassword = $passwordLine.Substring('MSSQL_SA_PASSWORD='.Length)
$env:FINCORE_BENCHMARK_SQLSERVER_CONNECTION_STRING = "Server=127.0.0.1,1433;Database=master;User Id=sa;Password=$saPassword;Encrypt=True;TrustServerCertificate=True;Pooling=True;Connect Timeout=30"

dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --filter "*PortfolioValuationBenchmarks*" --job short
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --e2e --samples 1000 --warmup 50
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --diagnostics --samples 1000 --warmup 50
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --translation-experiment --samples 100 --warmup 10
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --translation-validation --samples 100 --warmup 10

Remove-Item Env:\FINCORE_BENCHMARK_SQLSERVER_CONNECTION_STRING -ErrorAction SilentlyContinue
$saPassword = $null
```

The opt-in `--translation-experiment` compares the unchanged scalar-parameter
queries with query-local EF Core 10 JSON collection parameters. It uses one
ownership-verified database seeded with all four workloads; the parent keeps
the database lease until all BenchmarkDotNet workers and repeated
measurements finish. The worker processes verify the owner marker and never
create or drop the database. The experimental option is disabled by default,
and results are written under the ignored
`BenchmarkDotNet.Artifacts\TranslationExperiment\` directory.
The runner limits each variant/workload to at most 500 measured samples and
100 warmups.

The first end-to-end call per workload is reported separately as a client-cold
call after seeding; the SQL Server buffer pool may already be warm. The
specified warm-up calls are excluded from percentiles. End-to-end throughput
is the sample count divided by the sum of measured call durations;
each call creates a fresh DI scope and DbContext. The diagnostics mode is
separate because command interception affects execution and reports async
reader-command count and time to reader availability, not full EF
materialization time. No EF queries are parallelized.

If a process is interrupted, use only the exact generated database name
printed by its run and the same process-scoped connection setup:

```powershell
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --cleanup FinCoreBenchmarkRun_<GUID>
```

Cleanup requires the exact ignored run manifest, local SQL Server validation,
the matching owner marker, and both lifecycle locks. It refuses to delete a
database while its benchmark worker still holds a lease. See the
[benchmark methodology and measured baseline](docs/performance-benchmarking.md)
for the environment, actual results, and limitations.

The separate `--translation-validation` run exercises 1, 5, 50, 500, 2,000,
and 2,001 instruments with 12 trades per instrument, sparse/backdated prices,
future prices, and same-time price-ID ties. It checks SQL batches and exact
valuation equivalence in both collection modes, captures actual plans, and
runs BenchmarkDotNet allocation measurements plus paired end-to-end
percentiles. It keeps the existing 2,000-ID batches and the scalar production
default unchanged. Sample counts are bounded to 200 and warmups to 30. Its
separate reports and BDN output are written under the ignored
`BenchmarkDotNet.Artifacts\TranslationValidation\` directory.

## Build and test

Run from the repository root in PowerShell:

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --no-restore
dotnet test .\FinCore.slnx
```

The Prompt 5A validation completed with a clean build (0 warnings and 0
errors), 54 unit tests passed, and 14 opt-in SQL tests skipped in the
solution-wide run because no SQL test connection was supplied. The SQL
integration project was then run twice against fresh fixture-owned databases;
each run passed all 14 tests with 0 failures and 0 skips. The vulnerability
audit reported no vulnerable packages, and `git diff --check` passed.

The fixture cleaned up both run-owned databases. A read-only post-test check
found 0 remaining `FinCoreTestRun_` databases; `FinCore` still had 0 rows in
each portfolio table, and `FinCoreIntegrationTests` retained 3 portfolios,
6 instruments, 3 trades, and 9 market prices. Neither protected database was
used as an integration-test target.

Prompt 5B added the portfolio and trading REST API. Verification completed
with a build at 0 warnings and 0 errors, 62 unit/in-memory HTTP tests passing,
and 17 SQL Server integration tests passing in each of two fresh fixture runs
(0 failures and 0 skips in both runs). The SQL-backed HTTP tests asserted that
the API's resolved database name matched the current fixture-owned
`FinCoreTestRun_<GUID>` before sending requests. The dependency vulnerability
audit reported no vulnerable packages.

Prompt 6 adds `GET /api/portfolios/{id}/valuation`. Valuation uses one UTC
cutoff for historical trades and eligible prices, returns position-level
values and a complete USD total, and rejects missing prices without a partial
result. It uses batched no-tracking repository queries inside a read-only
`SERIALIZABLE` transaction, with checked decimal arithmetic. No migration,
schema, package, or existing Domain entity changes were needed.

Prompt 6 verification: the solution build succeeded with 0 warnings and 0
errors. The full solution run passed 78 unit/in-memory HTTP tests and skipped
20 opt-in SQL tests (0 failures). The SQL Server integration suite passed 20
tests on each of two fresh run-owned database runs (0 failures and 0 skips in
either run), including historical cutoff, deterministic price ties,
concurrent trade/price consistency, and the 80-share × USD 450 = USD 36,000
HTTP valuation. The NuGet vulnerability audit found no vulnerable packages,
and `git diff --check` was clean.

The subsequent publication-audit follow-up preserved Milestone 1 stock
pricing behavior for nonblank symbols longer than 20 characters while keeping
the 20-character limit for registered instruments. The updated build passed
with 0 warnings and 0 errors; 79 unit/in-memory HTTP tests and the fixture
endpoint guardrail test passed, while 20 opt-in SQL tests were skipped in the
solution run. Each of two isolated SQL runs passed all 20 tests with no
failures or skips. The SQL fixture accepted the IPv4-only local endpoint,
protected database row counts matched their prior values, and no run-owned
databases remained.

## Documentation

- [Architecture guide](docs/architecture.md) — project boundaries, design
  rationale, and end-to-end request flow.
- [Database design](docs/database-design.md) — entity relationships, SQL
  mappings, constraints, indexes, concurrency boundary, and migration status.
- [API documentation](docs/api-documentation.md) — endpoint contract,
  validation, response, and currency assumptions.
- [Class and method reference](docs/class-method-reference.md) — public
  FinCore types and methods, including parameters and return values.
- [Learning journal](docs/learning-journal.md) — completed milestones,
  learning topics, and validation commands.
- [Performance benchmarking](docs/performance-benchmarking.md) — SQL-only
  benchmark workloads, safety, measurement methodology, and measured baseline.
- [.github/copilot-instructions.md](.github/copilot-instructions.md) —
  repository-specific contribution expectations.

## Not implemented yet

Live market-data integration, stale-price threshold enforcement, currency
conversion, Redis caching, parallel valuation, Azure, and CI/CD remain
unimplemented. The SQL-only valuation baseline does not claim that parallel
processing or caching improves performance. The Docker SQL Server service is
local development infrastructure only. Any financial examples or fixtures
must use synthetic data.
