# SQL-only portfolio valuation performance baseline

## Status and scope

This is the Phase 3.1 SQL-only baseline for the existing portfolio valuation
service. It adds no Redis dependency, cache, parallel valuation logic, schema,
migration, API contract, or production valuation behavior. SQL Server remains
the source of truth. The benchmark invokes
`PortfolioValuationApplicationService` and preserves its sequential reads in
one read-only `SERIALIZABLE` transaction on one scoped `FinCoreDbContext`.

The benchmark database is not a test database and is not either protected
database. Provisioning, migration, and seeding happen before measurement. No
benchmark timing is a unit-test pass/fail threshold.

## Workloads and correctness checks

Each deterministic workload uses a fixed UTC cutoff of
`2025-01-02T12:00:00Z`, fixed synthetic identifiers, and synthetic equity
symbols. The benchmark sizes are 5, 50, 500, and 2,000 instruments. For every
instrument, setup inserts:

- a BUY of 100 shares and a SELL of 20 shares, both before the cutoff;
- an older USD market price;
- one USD market price at `2025-01-02T11:00:00Z`, eligible at the cutoff;
- one higher USD market price at `2025-01-02T12:01:00Z`, which is ineligible.

Eligible prices are `10.00 + instrumentIndex / 100`; the expected remaining
quantity is 80 shares per instrument, and the expected USD total is the sum
of `80 * eligiblePrice` across the portfolio. The harness verifies the
portfolio ID, USD currency, cutoff, all holdings, each selected price and
timestamp, and the total before benchmark output is accepted. This guards
against accidental future-price selection and incorrect position derivation.
The existing SQL tests separately cover same-time price-ID tie-breaking.

The 2,000-instrument size exercises the repository's existing bounded batch
limit without changing its query implementation.

## Database ownership and cleanup

The benchmark accepts only the process-scoped
`FINCORE_BENCHMARK_SQLSERVER_CONNECTION_STRING` configured for
`127.0.0.1,1433`, `Initial Catalog=master`, SQL authentication, encryption,
local certificate trust, and pooling. It validates that the connected server
is a sysadmin-enabled SQL Server 2022 Developer Edition instance before
creating anything. The connection string and password are never included in
benchmark output or manifests.

Each BenchmarkDotNet worker and each end-to-end run owns one
`FinCoreBenchmarkRun_<32-character-GUID>` database. Creation refuses existing
or protected names. The fixture:

1. Acquires a session-owned exclusive application lock in `master` for the
   exact run database name.
2. Writes a matching, non-secret run manifest under the ignored
   `BenchmarkDotNet.Artifacts/FinCoreBenchmarkRuns` directory.
3. Creates only its generated database and records an extended-property
   ownership marker containing the run GUID.
4. Acquires a second session-owned lease in the generated database and keeps
   it for the worker's entire lifetime.
5. Applies the existing EF Core migration and seeds synthetic data.
6. On normal cleanup, releases the database lease, verifies the exact name
   and marker, then drops only that database while retaining the master
   lifecycle lock.

The master lock prevents cleanup while a BenchmarkDotNet worker is active;
the database lease is an additional guard if the master session is lost.
Interrupted runs are not swept or automatically removed. The explicit
`--cleanup FinCoreBenchmarkRun_<GUID>` operation requires that exact run's
manifest, local-server validation, a matching marker, and both locks. If a
check fails, cleanup stops and leaves the database in place.

`FinCore` and `FinCoreIntegrationTests` are never benchmark write targets.
No Docker volume is removed.

## Running

From PowerShell at the repository root, read the SQL password from ignored
`.env` into the process environment without printing it. The complete command
sequence is in the [README benchmark instructions](../README.md#sql-only-portfolio-valuation-benchmarks).

```powershell
dotnet restore .\FinCore.slnx
dotnet build .\FinCore.slnx --configuration Release
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --filter "*PortfolioValuationBenchmarks*" --job short
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --e2e --samples 1000 --warmup 50
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --diagnostics --samples 1000 --warmup 50
```

BenchmarkDotNet uses its isolated worker process. Environment variables are
inherited by that process. Global setup creates and validates the database;
global cleanup drops it only after the benchmark worker finishes. Use the
explicit cleanup command for a known interrupted run, never a prefix-based
database cleanup.

## Measurement methodology

### BenchmarkDotNet

`PortfolioValuationBenchmarks` measures one warm invocation of the actual
SQL-backed Application service. A fresh DI scope and DbContext are created
for each invocation. `[MemoryDiagnoser]` reports allocations per operation
and BenchmarkDotNet reports mean, median, standard deviation, and its own
iteration statistics. These microbenchmark statistics are not presented as
p95 or p99 end-to-end latency.

BenchmarkDotNet's warm-up and measurement iterations run after the global
setup validation. Database creation, migration, and data insertion are
excluded.

### Repeated end-to-end mode

`--e2e` captures the first client-cold valuation after seed, validates it,
then executes the configured number of unmeasured warm-up calls. It measures
sequential Application-service calls, with fresh scopes, using
`Stopwatch.GetTimestamp`. The default is 50 warm-ups and 1,000 measured
samples per workload. The runner reports nearest-rank p50, p95, and p99 in
milliseconds, the sample count, warm-up count, first-call latency, and
sequential calls per second. The first call is client-cold only: the SQL
buffer pool may already be warm from seeding. The service call includes scope
creation, the valuation transaction and SQL reads, but not HTTP hosting,
database provisioning, migration, seeding, or output formatting.

Throughput is the number of measured calls divided by the sum of their
measured sequential durations; it is not a concurrent-load capacity claim.
Allocation results are provided by BenchmarkDotNet, not inferred from
per-thread allocation counters around asynchronous calls.

### SQL command diagnostics

`--diagnostics` runs separately with an EF Core `DbCommandInterceptor` and
reports async reader command count and cumulative time per valuation. Timing
ends when SQL returns the data reader and excludes EF row materialization.
Interceptor timings and counts are diagnostic data, not the primary latency
measurements, because interception changes the measured execution path.
Transaction begin/commit protocol operations that do not pass through
`DbCommand` are not counted as reader queries.

### Environment and reproducibility

The end-to-end report records the Git commit and dirty-worktree flag, OS,
.NET runtime, process architecture, CPU identifier and logical processor
count, installed RAM when available, SQL Server version/edition, workload
sizes, expected USD totals, sample count, and warm-up count. Build and
benchmark commands specify Release configuration. Compare only runs with
comparable machine power state, SQL Server image/version, runtime, database
state, workload, and warm-up. Keep the generated JSON and
`BenchmarkDotNet.Artifacts` out of Git unless a later milestone explicitly
chooses a sanitized, reproducible artifact format.

## Measured baseline

The following results were collected on 2026-11-19 in Release mode on the
environment recorded by the runner: Windows 11 build 10.0.26300, .NET
10.0.11, BenchmarkDotNet 0.15.8, Intel Core i7-8565U, 8 logical processors,
16 GiB RAM, and SQL Server 16.0.4325.4 Developer Edition. The working tree
was dirty because the benchmark changes were not committed. All correctness
checks passed and the run-owned database was cleaned up.

The repeated end-to-end run used 10 warm-up calls and 100 measured samples
per workload. Its p50/p95/p99 values are nearest-rank percentiles. The first
call is the client-cold sample reported separately; the SQL buffer pool may
already have been warm.

| Instruments | Expected total (USD) | Cold first call (ms) | Warm samples | p50 (ms) | p95 (ms) | p99 (ms) | Sequential calls/s |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 4,012.00 | 690.651 | 100 | 13.035 | 15.793 | 17.589 | 76.74 |
| 50 | 41,020.00 | 61.136 | 100 | 24.535 | 29.094 | 35.425 | 40.44 |
| 500 | 500,200.00 | 283.100 | 100 | 63.983 | 78.864 | 81.983 | 14.89 |
| 2,000 | 3,200,800.00 | 1,988.790 | 100 | 570.130 | 602.936 | 651.101 | 1.74 |

BenchmarkDotNet ShortRun used three warm-up and three measurement iterations:

| Instruments | Mean (ms) | Median (ms) | StdDev (ms) | Allocated |
| ---: | ---: | ---: | ---: | ---: |
| 5 | 12.931 | 12.437 | 1.270 | 226.04 KB |
| 50 | 20.170 | 20.184 | 0.453 | 839.68 KB |
| 500 | 118.187 | 121.547 | 7.359 | 38,227.99 KB |
| 2,000 | 627.316 | 626.140 | 3.241 | 574,419.16 KB |

An earlier 1,000-sample attempt was stopped by the existing SQL command
timeout during the 2,000-instrument workload. It produced no accepted
results and was not used in these tables. The completed 100-sample run is
the bounded baseline for this local machine; larger sample counts may require
an explicit future benchmark configuration or a query-performance
investigation rather than an increased timeout.

The separate diagnostics run used 100 calls and 10 warm-ups. It observed
4.00 reader commands per valuation and average SQL reader-execution time of
8.424 ms (5 instruments), 17.506 ms (50), 31.120 ms (500), and 197.985 ms
(2,000). These timings stop when SQL returns a reader and exclude EF
materialization.

BenchmarkDotNet mean, median, standard deviation, and allocated bytes per
operation, plus instrumented SQL command counts/durations, are recorded with
the actual run results. No comparative performance improvement is claimed
without a measured before/after comparison.

## Phase 3.2A stage diagnostics

The opt-in `--stage-diagnostics` mode registers a diagnostics sink with the
existing `PortfolioValuationApplicationService`; the default application
configuration does not register one. It measures the actual service stages
without changing financial calculations, SQL queries, transaction isolation,
or response semantics:

```powershell
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --stage-diagnostics --samples 100 --warmup 10
```

The report includes stage elapsed time, logical input/output counts, reader
command counts and reader execution time. Managed allocation values are
approximate process-wide allocation deltas captured around asynchronous stage
boundaries. They are diagnostic attribution only: asynchronous continuations,
EF Core, SqlClient, and runtime work can cross those boundaries. BenchmarkDotNet
`MemoryDiagnoser` remains authoritative for allocation-per-operation totals.

The diagnostic run also re-executes the captured workload queries with
`SET STATISTICS XML ON` against the same run-owned benchmark database and
saves the returned actual execution-plan XML in the diagnostic report. Plan
capture occurs before the fixture verifies ownership and drops the exact
database.

## Phase 3.2A measured diagnostics

The diagnostic run was performed on 2026-11-19 in Release mode with 100
measured calls and 10 warmups for each workload. It used the same isolated
database safeguards and deterministic workloads as Phase 3.1. The run-owned
database was `FinCoreBenchmarkRun_f374ba4dfae64b8c9b58e2aad6022aee` and was
cleaned up after actual plan capture.

| Instruments | Total ms | Trade ms | Instrument ms | Market-price ms | Position ms |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 9.915 | 2.415 | 2.488 | 2.880 | 0.037 |
| 50 | 21.205 | 3.361 | 3.628 | 11.915 | 0.138 |
| 500 | 66.242 | 7.703 | 24.528 | 31.426 | 0.571 |
| 2,000 | 637.665 | 26.266 | 293.156 | 313.484 | 1.711 |

At 2,000 instruments, the diagnostic attribution measured approximately
285,293 KB around market-price retrieval and 285,192 KB around instrument
retrieval, against approximately 574,744 KB for the total valuation stage.
These values strongly localize the scale-related cost to the two EF/SQL
materialization stages, but are not proof that those stages exclusively own
every allocation.

The stage counts were four reader commands per valuation:
portfolio, trades, instruments, and market prices. At 2,000 instruments the
stages returned 4,000 trades, 2,000 instruments, and 2,000 selected prices.
The synthetic database contains three prices per instrument, but the
historical query returns only the latest eligible row per instrument.

The captured actual plans showed:

- The trade valuation query used a clustered index scan on `PK_Trades` and a
  separate `Sort`; the existing
  `IX_Trades_Portfolio_Instrument_Execution` was not selected for this query.
- The market-price query used `IX_MarketPrices_Instrument_Latest` together
  with grouping/top-selection operators, plus a scan and sort component.
- The instrument lookup used the primary-key access path with a merge join over
  the parameter values and a sort.

This is evidence that the current trade index does not fully match the
valuation filter (`PortfolioId`, `ExecutedAt`) and ordering
(`ExecutedAt`, `Side`, `Id`). No index or schema change was made.

The 500-instrument BDN/end-to-end difference remains a measurement limitation,
not a resolved production cause. BDN and end-to-end runs use different worker
processes, database populations, timing statistics (mean versus p50), and
warmup/plan-cache histories. The stage run's 500-instrument total was 66.242
ms, close to the prior end-to-end p50 of 63.983 ms, while the separate BDN
mean was 118.187 ms. A controlled same-database BDN versus percentile run is
still required before attributing the difference to GC, query plans, or
database state.

### Confirmed root causes and remaining hypotheses

**Confirmed by this run:**

- Four sequential reader commands are used; there is no N+1 query behavior.
- `AsNoTracking()` is used by the valuation repositories.
- The 2,000 workload materializes 4,000 trades, 2,000 instruments, and 2,000
  selected prices.
- Trade retrieval incurs a clustered scan plus sort in the captured plan.
- Instrument and market-price retrieval/materialization dominate stage time and
  diagnostic allocation deltas at 500 and 2,000 instruments.
- Position grouping/sorting and final decimal/response work are less than 1%
  of total diagnostic duration in these synthetic workloads.

**Resolved by Phase 3.2B:** allocation stacks associate the large
character-array, `StringBuilder`, and string growth with EF Core's
`Contains`/`IN` parameter expansion and parameter-name uniquification in the
instrument and market-price retrieval queries.

**Still hypotheses requiring further measurement:**

- The exact allocation split between the instrument and market-price queries
  and the precise complexity class of EF's parameter processing.
- Whether SQL Server query-plan work or physical data layout is a significant
  part of the end-to-end latency growth.
- Whether the BDN discrepancy is caused by process/GC effects, database state,
  or plan-cache differences.

## Phase 3.2B targeted allocation profiling

The approved local profiler was `dotnet-trace` 10.0.750501, attached to
Release benchmark workers running on .NET 10.0.11. Each workload used the
existing ownership-verified `FinCoreBenchmarkRun_<GUID>` fixture and the
same deterministic seeded database for its paired modes. Provisioning,
migration, seeding, and correctness checks completed before tracing. Each
mode then ran 10 service calls after five warmups. The final measured loop
contains the service call and its fresh DI scope, but not the repeated
correctness-validation pass. The instrumented loop also resets its collector
before each call, so its phase time includes that reset. No connection string
or credential is stored in the trace reports.

Each trace was bounded to 45 seconds with a 64 MB buffer, the `gc-verbose`
profile, and allocation-stack collection. The trace parser filtered events
to the measured phase timestamps. The final 50-, 500-, and 2,000-instrument
traces contained 1,675, 12,350, and 165,474 events respectively; each had
call stacks, zero lost events, and zero allocation ticks without a stack.
The profile artifacts and per-phase JSON are preserved locally under the
Git-ignored `BenchmarkDotNet.Artifacts/AllocationTraces/` directory. Earlier
exploratory paired captures are also retained but are not used in the
measurements below because their timed loop included repeated correctness
validation.

`GCAllocationTick` byte totals are sampling-weighted estimates, not exact
allocation totals, live-heap sizes, or retained bytes. The independent
BenchmarkDotNet `MemoryDiagnoser` values remain the reliable allocation per
operation baseline. The trace estimates are directionally consistent with
that baseline:

| Instruments | Allocation ticks per 10 calls (plain / instrumented) | Sample-weighted bytes per call, approximate (plain / instrumented) | GC starts Gen0/Gen1/Gen2 per 10 calls (plain / instrumented) | Large-kind ticks per 10 calls (plain / instrumented) |
| ---: | ---: | ---: | ---: | ---: |
| 50 | 81 / 83 | 0.865 / 0.884 MB | 1/0/1 / 2/0/0 | 0 / 0 |
| 500 | 3,676 / 3,690 | 39.198 / 39.337 MB | 78/5/1 / 79/5/1 | 0 / 0 |
| 2,000 | 55,131 / 55,182 | 588.194 / 588.676 MB | 1,355/34/9 / 1,353/37/10 | 40 / 40 |

At 2,000 instruments, the 40 large-kind allocation samples corresponded to
about 7.02 MB of sample-weighted allocation per 10-call phase. This is
evidence of large-object allocation samples, not a measurement of total LOH
allocation or retained heap. The GC generation counts are captured runtime
`GC/Start` events within the phase windows; they are not a capacity or
pause-time result.

### Allocation evidence

The sampled type distribution was dominated by `System.Char[]`,
`System.Text.StringBuilder`, and `System.String`: about 93.6% of sampled
bytes at 500 instruments and 98.3% at 2,000 in the no-diagnostics phases.
The 50-instrument phase had only 81 allocation ticks, so its per-type shares
are too sparse for a strong distribution conclusion.

The call stacks identify EF Core SQL Server parameter processing as the
dominant source:

- At 50 and 500 instruments, the sampled `char[]`, builder, and string
  allocations repeatedly pass through
  `Microsoft.EntityFrameworkCore.Infrastructure.Uniquifier.Truncate` and
  `Uniquifier.Uniquify`.
- At 2,000 instruments, the dominant stacks pass through
  `SqlNullabilityProcessor`'s `ProcessInExpressionValues` / `VisitIn` path
  and `SqlServerSqlNullabilityProcessor.VisitIn`. Builder and string
  allocations appear under that same path.
- The two valuation queries that take instrument ID batches use
  `batch.Contains(...)`: instrument lookup and latest eligible market-price
  selection. Each receives up to 2,000 IDs, and the trace shows the
  per-execution `IN`-parameter expansion/name-uniquification work for these
  query shapes.

For the no-diagnostics 500-instrument phase (10 calls), the largest
sample-weighted type/stack groups were `System.Char[]` at about 139.2 MB,
`System.Text.StringBuilder` at 120.7 MB, and `System.String` at 102.3 MB;
their stacks pass through `Uniquifier.Truncate` / `Uniquifier.Uniquify`.
For the 2,000-instrument phase (10 calls), the largest groups passing through
`ProcessInExpressionValues` / `VisitIn` were `System.Char[]` at about
889.7 MB and 784.7 MB, followed by `System.Text.StringBuilder` at about
766.6 MB and 669.0 MB. These are GC allocation-tick sample-weighted bytes,
not exact allocated totals for those types or methods.

This explains the large allocation deltas previously localized
approximately to instrument retrieval and market-price retrieval at 2,000
instruments (about 285 MB each in Phase 3.2A). The profiles cover the whole
valuation, not individual repository-stage event ranges, so they do not
attribute exact byte totals to either query. The two query stacks and the
Phase 3.2A stage deltas together strongly identify parameter expansion as the
root allocation source; they do not prove every sampled string belongs to
only one repository call.

The Phase 3.1 BenchmarkDotNet allocations grow about 45.5x from 50 to 500
instruments (10x the IDs) and 15.0x from 500 to 2,000 (4x the IDs). This is
measured superlinear growth consistent with the profiled expansion and
uniquification work. These workload points do not establish the algorithm's
asymptotic complexity or prove which internal EF operation causes that
scaling.

`Microsoft.Data.SqlClient.SqlParameter`, command construction, and network
reader allocations occur in the traces but are small compared with the
EF-generated character arrays and strings. Trade entity materialization,
position calculation, final multiplication/summation, and response
construction are not supported as the source of the hundreds of megabytes:
the Phase 3.2A position calculation was 1.711 ms, the execution used four
reader commands rather than N+1 reads, and the high-workload allocation
stacks are dominated by EF `IN` processing rather than Domain entities.
Repositories use `AsNoTracking()`. The traces do not show initial query
compilation as the dominant hot path; the measured, warmed executions are
dominated by per-execution SQL nullability processing and parameter naming.

### Diagnostics comparison and limitations

| Instruments | Traced phase ms/call (plain / diagnostics + interceptor) |
| ---: | ---: |
| 50 | 22.661 / 21.243 |
| 500 | 136.939 / 138.654 |
| 2,000 | 731.415 / 866.744 |

These are 10-call trace-window timings, not benchmark latency results. Mode
order was always plain then instrumented, the sample count was small, and
`dotnet-trace` itself adds runtime overhead. The allocation estimates for
the two modes differed by about 0.35% at 500 and 0.08% at 2,000; the 50-case
difference is within the uncertainty of only 81/83 allocation ticks. This
shows no material allocation change at the larger sizes in this capture, but
does not establish an exact instrumentation overhead. Timing differences are
inconsistent across sizes and cannot establish a latency effect.

The 500-instrument BDN mean versus end-to-end p50 difference remains
unresolved. The traced service calls were about 136.9 ms plain, but were
collected under GC tracing and are not comparable to the untraced 118.187 ms
BDN mean or 63.983 ms end-to-end p50. BDN used a ShortRun, while the
end-to-end run used a separate process, database, 100 samples, and percentile
statistics. A matched, untraced comparison is still required.

The profiler's event names use `GC/Start`; generation counts were mapped from
the `Depth` payload. Allocation type byte totals are sampled estimates,
including the large-kind values, and must not be presented as exact per-type
allocation totals. The profiler does not assign allocations to individual
service stages. No actual execution plan was captured during this phase; the
Phase 3.2A isolated plans remain the plan evidence. No package was added to
the solution for profiling, and no query, index, transaction, schema, API,
financial rule, Redis, or Docker change was made for this phase.

### Findings and follow-up

**Confirmed:** repeated large string and character-array allocations are
associated with EF Core's `Contains`/`IN` parameter expansion and parameter
name uniquification in both ID-based retrieval queries. Allocation growth
from 50 to 500 and from 500 to 2,000 is consistent with the increasing
parameter-list work. SQL row materialization, trade processing, and position
calculation are not supported as the dominant source by the sampled stacks.

**Not established:** the exact allocation split between the instrument and
price queries, the precise complexity class of EF's parameter processing,
the exact cause of the BDN/end-to-end timing difference, and production-scale
network or SQL Server impact.

**Low-risk next experiments, requiring separate approval:** first compare a
smaller ID batch size as a controlled benchmark-only experiment, measuring
both total allocations and the extra SQL round trips. Then prototype a
server-side set-input approach (for example a table-valued parameter or a
supported JSON parameter) in an isolated branch and compare correctness,
query plans, allocations, and matched end-to-end timings. A table-valued
parameter requires a database type/schema deployment decision; neither
approach is implemented here. Preserve sequential EF operations on the
scoped context.

## Phase 3.2C controlled EF Core 10 collection-parameter experiment

This is a query-local, opt-in experiment; the scalar `Contains` translation
remains the production default. `PortfolioCollectionParameterOptions` defaults
`UseJsonParameterForIds` to `false`. When enabled only in the experiment
provider, the two confirmed batch-ID predicates use
`EF.Parameter(batch).Contains(...)`. There is no global EF translation change,
new package, schema or migration change, trade-path change, Redis, or
parallel EF query.

The Release run on 2026-11-19 used BenchmarkDotNet 0.15.8, .NET SDK 10.0.303,
.NET 10.0.11, Windows 11 build 10.0.26300, an Intel Core i7-8565U, 8 logical
processors, 16 GiB RAM, and SQL Server 16.0.4325.4 Developer Edition. Its one
run-owned database, `FinCoreBenchmarkRun_49b30edf221248c282f996981ad8035b`,
reported compatibility level 160. It was seeded once with all four unchanged
deterministic workloads and shared by the parent coordinator and BDN worker
processes. The owner marker was checked by each worker, and the parent held
the fixture lease through the entire run. The database was dropped after
measurements and verification; the protected database snapshots and the set
of pre-existing run-owned databases were unchanged.

Before measurement, both modes were executed and the valuation's portfolio,
all positions, eligible price timestamp and amount, cutoff, currency, and
expected total were verified for 5, 50, 500, and 2,000 instruments. The
captured SQL confirmed that the market-price predicate retained
`AsOfUtc <= cutoff` and descending `AsOfUtc`/`Id` ordering. The existing SQL
integration test for historical cutoffs and timestamp/ID tie-breaking now
executes in both modes; the JSON-mode missing-price failure is also covered.
All 22 SQL integration tests passed.

### Generated SQL and actual plans

The default translation uses one scalar parameter per ID: `N` parameters for
instrument lookup and `N + 1` for latest market prices (including the UTC
cutoff). JSON mode uses one JSON collection parameter for either query, plus
the cutoff for market prices. In the market-price SQL EF emits two `OPENJSON`
row sources that reference the same JSON parameter in its two translated
subqueries; it does not send two JSON parameters. The instrument query emits
one `OPENJSON` row source. Parameter values were checked against the exact
synthetic instrument ID set, and no credentials or parameter values are
written to the SQL-shape report.

Actual plans were captured for both variants at 500 and 2,000 instruments
against the run-owned database using `SET STATISTICS XML ON`:

| Workload / query | Scalar plan | JSON plan | Estimated subtree cost (scalar → JSON) |
| --- | --- | --- | ---: |
| 500 / instruments | Clustered index scan on `PK_Instruments` | `OPENJSON` then clustered index seek on `PK_Instruments` | 0.0622 → 0.0306 |
| 500 / market prices | Index scan on `IX_MarketPrices_Instrument_Latest` and clustered scan on `PK_MarketPrices` | Index seeks on `IX_MarketPrices_Instrument_Latest` and clustered seeks | 0.2560 → 0.0887 |
| 2,000 / instruments | Clustered index scan on `PK_Instruments` | `OPENJSON` then clustered index seek on `PK_Instruments` | 0.0942 → 0.0306 |
| 2,000 / market prices | Index scan on `IX_MarketPrices_Instrument_Latest` and clustered scan on `PK_MarketPrices` | Index seeks on `IX_MarketPrices_Instrument_Latest` and clustered seeks | 0.3926 → 0.0887 |

These are optimizer cost units, not elapsed time. The actual 2,000-instrument
scalar instrument scan read 2,555 rows; the JSON seek read the 2,000 matching
rows. The scalar market-price plan read 7,665 rows from both the latest-price
index and clustered index before filtering; JSON mode used seeks, reading
4,000 eligible price rows per source at this workload. No spills or plan
warnings were present in these captured plans. The plan shape and measured
reader times showed no regression for the tested workloads.

One remaining plan caveat is the JSON table-valued-function cardinality
estimate: SQL Server estimated 50 IDs for `OPENJSON` at both 500 and 2,000
instruments, versus actual row counts of 500 and 2,000. That is a 40x
underestimate at the largest measured size. The observed plans still chose
seeks and were faster here, but larger data volumes or different distributions
could change plan quality. Keep cardinality-sensitive validation in any
future larger-workload comparison.

### BenchmarkDotNet results

The paired BDN run used one seeded database for both modes, a ShortRun with
three launches, three warm-up iterations per launch, and three measured
iterations per launch (nine measured observations per case). `--allStats`
reported mean, median, standard deviation, and GC counts. Values below are
per valuation; allocations are BenchmarkDotNet `MemoryDiagnoser` values, not
sampled profiler estimates.

| Instruments | Mode | Mean (ms) | Median (ms) | StdDev (ms) | Allocated (KB) | Gen0 / 1,000 ops | Gen1 / 1,000 ops | Gen2 / 1,000 ops |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | Scalar | 13.751 | 12.880 | 1.546 | 225.46 | 46.875 | 0 | 0 |
| 5 | JSON | 12.965 | 12.799 | 0.845 | 190.80 | 46.875 | 0 | 0 |
| 50 | Scalar | 32.657 | 32.235 | 1.494 | 832.07 | 200.000 | 66.667 | 0 |
| 50 | JSON | 18.062 | 18.149 | 0.793 | 346.31 | 62.500 | 0 | 0 |
| 500 | Scalar | 94.058 | 87.192 | 18.781 | 38,230.95 | 8,750 | 2,250 | 250 |
| 500 | JSON | 46.946 | 43.590 | 6.375 | 1,931.11 | 300 | 100 | 0 |
| 2,000 | Scalar | 803.747 | 783.105 | 68.140 | 574,418.46 | 138,000 | 13,000 | 0 |
| 2,000 | JSON | 191.604 | 185.066 | 49.881 | 7,386.88 | 1,000 | 666.667 | 0 |

At 500 instruments, allocated bytes fell by 94.9%; at 2,000 they fell by
98.7% (574,418.46 KB to 7,386.88 KB per valuation, using BDN's 1 KB =
1,024-byte units). The BDN median
improved by about 50.0% at 500 and 76.4% at 2,000. At 5 instruments the
median latency was effectively unchanged, while allocation fell 15.4%.
BDN flagged one outlier for the 500-scalar and 2,000-JSON cases. ShortRun
statistics remain less stable at the largest size, so use the paired
end-to-end distribution as the primary latency comparison.

### Paired end-to-end latency and SQL diagnostics

The repeated run alternated which variant went first within each paired
sample. Each variant received 10 warmups and 100 measured valuations for
every workload on the same database. Calls were sequential and used fresh DI
scopes; correctness validation was outside the timed interval. Throughput is
the 100 calls divided by the summed duration for that variant.

| Instruments | Mode | Samples | p50 (ms) | p95 (ms) | p99 (ms) | Sequential calls/s |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 5 | Scalar | 100 | 16.028 | 20.197 | 21.302 | 61.17 |
| 5 | JSON | 100 | 16.073 | 19.955 | 23.542 | 61.90 |
| 50 | Scalar | 100 | 25.145 | 30.442 | 33.785 | 38.34 |
| 50 | JSON | 100 | 15.325 | 18.577 | 20.145 | 62.80 |
| 500 | Scalar | 100 | 74.056 | 86.121 | 89.229 | 13.26 |
| 500 | JSON | 100 | 35.116 | 40.290 | 46.743 | 27.98 |
| 2,000 | Scalar | 100 | 588.181 | 716.436 | 734.972 | 1.67 |
| 2,000 | JSON | 100 | 101.355 | 127.314 | 137.380 | 9.59 |

The p50 was effectively unchanged at 5 instruments, and improved by 39.1%,
52.6%, and 82.8% at 50, 500, and 2,000 instruments respectively. The 2,000
case changed from 1.67 to 9.59 sequential calls/s. These are local,
synthetic, sequential measurements, not production capacity claims.

Using the same database narrowed one comparison confound but did not make
BDN and end-to-end timings identical. At 500 instruments, the BDN medians
(87.192 ms scalar, 43.590 ms JSON) were about 17.7% and 24.1% above their
end-to-end p50s (74.056 ms and 35.116 ms). At 2,000, the BDN medians
(783.105 ms and 185.066 ms) were about 33.1% and 82.5% above the paired
end-to-end p50s (588.181 ms and 101.355 ms). The BDN cases run in child
processes and include `MemoryDiagnoser`; the end-to-end loop runs in the
coordinator process and follows BDN, so process/GC state, query-plan or
memory-grant feedback, and measurement harness differences remain possible
causes. The separate approximate transaction-stage diagnostics (68.351/32.348
ms at 500 and 636.741/103.615 ms at 2,000, scalar/JSON) align more closely
with the end-to-end p50s. The available measurements do not isolate the
cause; do not infer a production latency penalty or discount the matched-mode
comparison from this discrepancy alone.

SQL diagnostics ran separately from BDN and the end-to-end measurements.
Every valuation used four reader commands in both modes, so the change did
not add round trips or create N+1 behavior. Reader execution ends when SQL
returns the reader and excludes EF materialization.

| Instruments | Mode | Reader commands/call | Total reader ms/call | Instrument reader ms | Market-price reader ms |
| ---: | --- | ---: | ---: | ---: | ---: |
| 5 | Scalar | 4.00 | 6.538 | 1.652 | 1.768 |
| 5 | JSON | 4.00 | 6.333 | 1.546 | 1.762 |
| 50 | Scalar | 4.00 | 16.274 | 2.297 | 10.343 |
| 50 | JSON | 4.00 | 8.375 | 2.008 | 2.749 |
| 500 | Scalar | 4.00 | 34.601 | 9.120 | 17.584 |
| 500 | JSON | 4.00 | 19.056 | 3.721 | 6.671 |
| 2,000 | Scalar | 4.00 | 229.663 | 97.685 | 112.517 |
| 2,000 | JSON | 4.00 | 41.896 | 8.959 | 13.143 |

Approximate stage allocation deltas (not exact ownership) fell at 2,000
instruments from 285,190 KB to 1,778 KB around instrument retrieval and from
285,295 KB to 1,495 KB around market-price retrieval. The measured total
valuation stage fell from approximately 574,745 KB to 7,522 KB. These
instrumented deltas agree directionally with BDN, which remains authoritative
for per-operation allocation.

### Outcome and limitations

Keep the experiment as an opt-in Infrastructure setting; do not promote it
to the production default without explicit approval. It removes the profiled
scalar-parameter expansion hotspot, materially reduces allocations at 500
and 2,000 instruments, lowers reader execution duration, and produced
correct totals and non-regressed plans on this SQL Server 2022 workload.
The 5-instrument results show no material latency gain, and `OPENJSON` plan
estimates understate the tested large collection sizes. A later approval
should decide whether to retain/refine the adapter after validation on
larger, more representative data and a production-like SQL configuration.

BDN allocated bytes and GC counts are per-operation diagnoser results; stage
allocation numbers are approximate process-wide deltas. BDN uses nine
observations per case and produced outliers at two cases. The end-to-end
numbers use 100 sequential samples, and the SQL timings are separately
instrumented; none should be substituted for another. The first query-plan
and generated-SQL JSON files, BDN exports, and full run report are retained
locally under the Git-ignored
`BenchmarkDotNet.Artifacts/TranslationExperiment/run-20261119T190642209Z-10bd24c1a9894610b38b5ae09668447a/`
directory. Phase 3.1 and profiling artifacts remain unchanged.

## Phase 3.2D final EF Core collection-translation validation

The separate `--translation-validation` run compared scalar and JSON modes
without changing the Infrastructure default, query semantics, transaction
isolation, schema, or existing 2,000-ID batch size. It used the
ownership-verified `FinCoreBenchmarkRun_79d18c69d6b540d4894bc7a4dc0df5be`
database at compatibility level 160. The fixture removed the database after
the run; the protected-database snapshot and pre-existing owned-database set
were unchanged.

The deterministic datasets contained 1, 5, 50, 500, 2,000, and 2,001
equities, with 12 ordered trades (six buys and six sells) per instrument.
Market-price history varied deliberately: one eligible latest price per
instrument, older backdated rows for one-third, same-time competing IDs for
roughly one-eleventh, and future prices for one-fifth. Exact decimal
quantities, selected prices, timestamps, currency, cutoff, positions, and
portfolio totals were verified in both modes. SQL integration coverage also
verified missing prices, decimal precision, checked overflow, exact-cutoff
inclusion, future-price exclusion, deterministic ID tie-breaking,
cancellation during instrument retrieval, and an atomic trade/price write
concurrent with valuation in both modes.

### Translation and batching verification

Both repositories queried each expected instrument ID exactly once. Captured
command shapes were:

| Workload | Scalar instrument / price IDs | JSON instrument / price ID parameters | Reader commands per valuation |
| ---: | --- | --- | ---: |
| 1 | 1 / 1 | 1 / 1 | 4 |
| 5 | 5 / 5 | 1 / 1 | 4 |
| 50 | 50 / 50 | 1 / 1 | 4 |
| 500 | 500 / 500 | 1 / 1 | 4 |
| 2,000 | 2,000 / 2,000 | 1 / 1 | 4 |
| 2,001 | 2,000 + 1 / 2,000 + 1 | 1 + 1 / 1 + 1 | 6 |

Price commands additionally carried one cutoff parameter (scalar parameter
counts were `N + 1` per price batch; JSON price commands carried one JSON
collection and one cutoff). The market-price SQL retained inclusive
`AsOfUtc <= cutoff` and descending `AsOfUtc`, then `Id` ordering. Its JSON
translation referenced one JSON parameter from two `OPENJSON` row sources.
At 2,001 IDs both modes retained two batches per repository; all positions
and totals matched exactly. No query exceeded the existing 2,000-ID
boundary.

### Actual SQL plans

The runner captured 28 actual plans for the instrument and market-price
queries against the isolated database. Scalar plans used seeks at small
counts, but switched to clustered/index scans (and sort/hash operators in
the price query) at 500 and 2,000 IDs. JSON plans used nested loops and
clustered/market-price index seeks at every tested size. The 2,001 workload
used the 2,000-ID plan plus a one-ID batch plan. Captured JSON plan shapes
were consistent across input sizes; scalar SQL shapes and plans varied with
the expanded parameter count.

SQL Server estimated each `OPENJSON` row source at 50 rows regardless of its
actual input: 1, 5, 50, 500, or 2,000 IDs. At 2,001, the two batches had
actual inputs of 2,000 and 1, while both plans still estimated 50. The 500
and 2,000 estimates therefore understate the input by 10x and 40x. The
captured plans still chose seeks. There were no plan warnings, spill
operators, or plan-affecting-conversion warnings. At 2,000 IDs, the scalar
market-price plan requested 7,528 KB and used 1,120 KB; the JSON plan
requested 1,024 KB and used 256 KB. These are isolated-run observations,
not proof that different table sizes or distributions will retain these
plans.

### BenchmarkDotNet

BenchmarkDotNet 0.15.8 used a ShortRun with three launches and three measured
iterations per case (nine observations). The richer trade distribution
increases allocation versus the Phase 3.2C two-trade workload, so compare
scalar and JSON only within each row; do not compare these allocation totals
directly with the earlier baseline.

| Instruments | Mode | Mean (ms) | Median (ms) | StdDev (ms) | Allocated (KB/op) |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1 | Scalar | 13.79 | 13.80 | 0.741 | 205.33 |
| 1 | JSON | 16.05 | 15.63 | 1.474 | 186.31 |
| 5 | Scalar | 17.99 | 18.29 | 0.828 | 251.54 |
| 5 | JSON | 13.80 | 13.41 | 1.091 | 211.06 |
| 50 | Scalar | 21.88 | 21.92 | 1.276 | 1,082.78 |
| 50 | JSON | 25.08 | 25.11 | 2.512 | 597.07 |
| 500 | Scalar | 216.07 | 218.37 | 37.638 | 40,788.98 |
| 500 | JSON | 95.21 | 95.69 | 4.619 | 4,481.05 |
| 2,000 | Scalar | 749.75 | 744.25 | 16.755 | 584,587.20 |
| 2,000 | JSON | 261.83 | 264.96 | 11.428 | 17,587.48 |
| 2,001 | Scalar | 877.74 | 873.22 | 54.242 | 584,656.35 |
| 2,001 | JSON | 424.52 | 360.55 | 106.642 | 17,635.50 |

At 500, JSON reduced BDN allocations by 89.0%; at 2,000 and 2,001, by
approximately 97.0%. The means were lower for JSON at 5, 500, 2,000, and
2,001, but higher at 1 and 50; the small-case latency evidence is not
uniform. The 2,001 JSON case had substantial spread and a 614.02 ms maximum,
so its ShortRun result is uncertain.

### Paired end-to-end and reader timings

For each mode and size, the runner recorded 100 timed calls after 10 warmups.
Variants alternated order. Calls were sequential, used fresh scopes, and
validated returned valuations outside the timed interval. Calls/second is
the sample count divided by summed call duration. A command interceptor
collected reader counts and durations; this adds some instrumentation
overhead, equally applied to both variants, so compare only within this run.
The first call from a new DI provider is reported separately in the JSON
report, but it is not a cold SQL measurement: translation checks and
actual-plan capture had already executed the queries. SQL buffer and plan
caches were deliberately not cleared.

| Instruments | Mode | p50 (ms) | p95 (ms) | p99 (ms) | Mean (ms) | StdDev (ms) | Reader commands | Reader ms/call | Calls/s |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Scalar | 20.067 | 31.630 | 36.807 | 20.695 | 5.514 | 4 | 12.419 | 48.32 |
| 1 | JSON | 19.491 | 28.799 | 35.104 | 19.893 | 4.938 | 4 | 12.431 | 50.27 |
| 5 | Scalar | 14.499 | 22.933 | 38.968 | 15.716 | 5.831 | 4 | 9.844 | 63.63 |
| 5 | JSON | 14.258 | 24.020 | 34.359 | 15.310 | 4.910 | 4 | 9.640 | 65.32 |
| 50 | Scalar | 30.284 | 43.597 | 62.220 | 31.963 | 7.158 | 4 | 20.904 | 31.29 |
| 50 | JSON | 28.631 | 43.691 | 65.114 | 30.920 | 11.093 | 4 | 20.588 | 32.34 |
| 500 | Scalar | 121.306 | 147.967 | 176.783 | 122.304 | 16.564 | 4 | 64.890 | 8.18 |
| 500 | JSON | 79.168 | 101.553 | 115.087 | 82.507 | 19.514 | 4 | 47.677 | 12.12 |
| 2,000 | Scalar | 849.555 | 1,753.228 | 2,043.072 | 944.932 | 317.170 | 4 | 370.520 | 1.06 |
| 2,000 | JSON | 250.749 | 459.759 | 636.550 | 279.640 | 90.515 | 4 | 131.935 | 3.58 |
| 2,001 | Scalar | 893.760 | 1,777.108 | 2,193.993 | 1,067.927 | 371.330 | 6 | 418.258 | 0.94 |
| 2,001 | JSON | 260.279 | 572.418 | 746.633 | 311.299 | 113.843 | 6 | 147.878 | 3.21 |

The 1- and 5-instrument p50s were effectively unchanged. At 50, JSON's p50
was lower but its p99 was slightly higher; the small-workload results do not
establish a consistent latency advantage. At 500, 2,000, and 2,001, JSON
reduced p50 by approximately 34.8%, 70.5%, and 70.9% respectively, while
reader duration also fell. At 2,000 and 2,001, the p99s remain sensitive to
the 100-sample tails and the high observed standard deviations.

### Decision and remaining risks

Retain JSON as an opt-in experiment; do not promote it to the production
default. The large-workload allocation and latency improvements are
substantial and correctness tests passed, but the small-workload BDN latency
is mixed, JSON cardinality estimates remain fixed at 50, and this is one
local SQL Server Developer run with synthetic data. More repeated runs and
production-like data/server conditions are needed before a default change.
Keep the 2,000-ID batching conservatively: 2,001 IDs correctly required two
batches and six reader commands in both modes, but this validation does not
establish that JSON intrinsically requires that limit. No workload larger
than 2,001 instruments was run.

The run's SQL shapes, actual plans, validation report, and BDN output remain
in the ignored
`BenchmarkDotNet.Artifacts/TranslationValidation/run-20261119T195956646Z-db26e1ebdd464868b37ae4ae3816ffbc/`
directory. The previous Phase 3.1, 3.2B, and 3.2C artifacts were not
overwritten.

### Benchmark harness result validation

The default SQL-only BenchmarkDotNet entry point derives its expected cases
from BenchmarkDotNet's parsed job and filters, checks the returned case matrix
and required measurements, and returns a nonzero exit code for failed or
incomplete runs. Explicit launch counts are checked exactly; when
BenchmarkDotNet chooses the launch count automatically, the harness requires
at least one successful launch and relies on BenchmarkDotNet's report status.
The selected artifact directory receives a unique `INCOMPLETE-<id>.txt`
marker until validation succeeds and a `FAILED-<id>.txt` marker on failure.
BenchmarkDotNet's help, information and benchmark-listing modes remain available.

The translation experiment and final validation reject BenchmarkDotNet
summaries unless every expected workload and translation mode has exactly one
successful report, all three worker launches succeed with exit code zero,
and timing and allocation results are present and valid. This check occurs
before the run's experiment or validation report is written. `INCOMPLETE.txt`
marks the run until all measurements, database cleanup and report generation
finish. BenchmarkDotNet may export partial diagnostics before it returns;
its artifact directory also retains an incomplete marker and a `FAILED.txt`
marker when validation rejects the run. A process interrupted during
measurement retains the incomplete marker. Existing run artifacts are not
changed.

Run the database-free harness regression simulation with:

```powershell
dotnet run --configuration Release --project .\benchmarks\FinCore.Benchmarks\FinCore.Benchmarks.csproj -- --harness-self-test
```

The simulation runs real successful and intentionally failing BenchmarkDotNet
workers, checks missing and duplicate cases, a nonzero worker exit with
measurements, missing summaries or launches, and missing allocation data, and
verifies both default-entry process exit codes. Its ignored artifacts include
intentional failures and are not valuation benchmark results.
To additionally exercise fixture cleanup after a failed worker, configure the
same local SQL connection used for benchmarks and run
`--harness-sql-failure-test`. This creates and disposes one ownership-verified
benchmark database and compares the protected and owned database snapshots.
The separate `--harness-setup-failure-test` injects failure immediately after
the baseline fixture is initialized, checks that the owned database was
cleaned up, and verifies that protected names and a name without an ownership
manifest are refused. Setup exceptions trigger cleanup when ownership is
verified; if ownership cannot be established, the database is not dropped.
Interrupted processes still require the documented manifest-based recovery.

## Limitations

- The baseline is a local SQL Server 2022 Developer instance reached over
  loopback, not a production network or production-scale dataset.
- Synthetic data has a fixed simple trade and price-history shape; it does not
  represent every workload or market-data distribution.
- End-to-end calls are sequential. The Phase 3.2D integration test exercises
  transaction consistency against one concurrent write, but no parallel
  valuation or concurrent-load capacity benchmark is implemented.
- The service-level measurement excludes HTTP hosting and client/network
  latency.
- The first call has a cold client path but may benefit from SQL Server's
  warmed buffer pool.
- Command interceptor timings stop at reader availability and exclude EF
  materialization.
- Redis, cache consistency, cache failure behavior, and parallel processing
  remain unimplemented and unmeasured.
