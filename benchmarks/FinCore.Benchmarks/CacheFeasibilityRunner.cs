using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class CacheFeasibilityRunner
{
    private const long MaximumTraceBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromMinutes(20);

    internal static async Task RunAsync(bool pilot)
    {
        int samples = pilot ? 6 : 100;
        int warmup = pilot ? 2 : 10;
        CacheScenario[] scenarios = pilot
            ? [new CacheScenario("pilot", 5, 50, false, 2)]
            : CacheFeasibilityWorkload.Scenarios;
        Guid runId = Guid.NewGuid();
        string directory = Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts",
            "CacheFeasibility", $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{runId:N}");
        Directory.CreateDirectory(directory);
        string incomplete = Path.Combine(directory, "INCOMPLETE.txt");
        await File.WriteAllTextAsync(incomplete, "INCOMPLETE: feasibility run has not been validated.");
        ProtectedDatabaseSnapshot before = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedBefore = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        string? databaseName = null;
        List<CacheScenarioResult> results = [];
        Exception? failure = null;
        Stopwatch runClock = Stopwatch.StartNew();
        DateTimeOffset anchor = DateTimeOffset.UtcNow;
        try
        {
            await using BenchmarkDatabaseFixture fixture = new();
            await fixture.InitializeAsync();
            databaseName = fixture.DatabaseName;
            string tracePath = Path.Combine(directory, "selection-trace.jsonl.gz");
            await using FileStream traceFile = new(tracePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await using GZipStream compressed = new(traceFile, CompressionLevel.Fastest);
            await using StreamWriter trace = new(compressed, new UTF8Encoding(false));
            foreach (CacheScenario scenario in scenarios)
            {
                CacheFeasibilityWorkload workload = new(scenario, anchor);
                await workload.SeedAsync(fixture.BenchmarkConnectionString);
                results.Add(await MeasureScenarioAsync(
                    fixture.BenchmarkConnectionString, workload, samples, warmup, runId, trace, traceFile, runClock));
            }
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        try
        {
            ProtectedDatabaseSnapshot after = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
            IReadOnlyList<string> ownedAfter = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
            if (before != after || !ownedBefore.SequenceEqual(ownedAfter, StringComparer.Ordinal) ||
                (databaseName is not null && ownedAfter.Contains(databaseName, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Protected state or owned-database cleanup verification failed.", failure);
            }
            if (failure is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
            if (databaseName is null || results.Count != scenarios.Length)
            {
                throw new InvalidOperationException("The feasibility run did not complete its expected scenarios.");
            }

            var report = new
            {
                Status = "COMPLETE",
                RunId = runId,
                ScheduleVersion = CacheFeasibilitySchedule.Version,
                Pilot = pilot,
                DatabaseName = databaseName,
                AnchorUtc = anchor,
                Samples = samples,
                Warmup = warmup,
                SqlCold = "Not measured; first-client calls use a running SQL Server with uncontrolled caches.",
                Allocation = "Approximate process-wide GC deltas; includes diagnostic overhead.",
                Oracle = "Empty initial cache; unlimited capacity; no expiry; perfect per-instrument invalidation; one interval per instrument; no Redis timing.",
                Trace = "selection-trace.jsonl.gz",
                Scenarios = results
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            File.Delete(incomplete);
            Console.WriteLine($"COMPLETE: {directory}");
            foreach (CacheScenarioResult result in results)
            {
                Console.WriteLine($"{result.Scenario.Name}: n={result.Samples}, " +
                    $"price mean scalar/json={result.Scalar.PriceMeanMs:F3}/{result.Json.PriceMeanMs:F3} ms, " +
                    $"e2e p50={result.Scalar.P50Ms:F3}/{result.Json.P50Ms:F3} ms, " +
                    $"exact/interval hits={result.Oracle.ExactHits}/{result.Oracle.IntervalHits} of {result.Oracle.Distinct}");
            }
        }
        catch
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "FAILED.txt"),
                "FAILED: feasibility results are incomplete; do not treat them as successful.");
            Console.Error.WriteLine($"FAILED: feasibility run; inspect {directory}");
            throw;
        }
    }

    private static async Task<CacheScenarioResult> MeasureScenarioAsync(
        string connectionString, CacheFeasibilityWorkload workload, int samples, int warmup,
        Guid runId, StreamWriter trace, FileStream traceFile, Stopwatch runClock)
    {
        SqlCommandDiagnosticsInterceptor scalarSql = new();
        SqlCommandDiagnosticsInterceptor jsonSql = new();
        ValuationDiagnosticsCollector scalarDiagnostics = new(scalarSql);
        ValuationDiagnosticsCollector jsonDiagnostics = new(jsonSql);
        CacheFeasibilityObservation scalarObservation = new();
        CacheFeasibilityObservation jsonObservation = new();
        await using ServiceProvider scalar = BenchmarkServiceProviderFactory.Create(
            connectionString, scalarSql, scalarDiagnostics, priceObservation: scalarObservation);
        await using ServiceProvider json = BenchmarkServiceProviderFactory.Create(
            connectionString, jsonSql, jsonDiagnostics, true, jsonObservation);
        CacheFeasibilityOracle oracle = new();
        List<CacheMeasurement> scalarSamples = [];
        List<CacheMeasurement> jsonSamples = [];

        DateTimeOffset firstCutoff = Cutoff(workload.Scenario);
        CacheMeasurement scalarFirst = await MeasureAsync(scalar, workload, workload.PortfolioA,
            firstCutoff, scalarDiagnostics, scalarObservation);
        CacheMeasurement jsonFirst = await MeasureAsync(json, workload, workload.PortfolioA,
            firstCutoff, jsonDiagnostics, jsonObservation);
        ValidatePair(scalarFirst, jsonFirst);
        double scalarFirstMs = scalarFirst.ElapsedMs;
        double jsonFirstMs = jsonFirst.ElapsedMs;
        await trace.WriteLineAsync(JsonSerializer.Serialize(new
        {
            RunId = runId, Scenario = workload.Scenario.Name, Phase = "first-client", Block = 0,
            Sample = 0, Portfolio = workload.PortfolioA, ModeOrder = "scalar-first", Cutoff = firstCutoff
        }));

        for (int i = 0; i < warmup; i++)
        {
            CacheScheduleSlot slot = CacheFeasibilitySchedule.At(i);
            DateTimeOffset cutoff = Cutoff(workload.Scenario);
            Guid portfolio = slot.PortfolioA ? workload.PortfolioA : workload.PortfolioB;
            if (!slot.JsonFirst)
            {
                scalarFirst = await MeasureAsync(scalar, workload, portfolio, cutoff, scalarDiagnostics, scalarObservation);
                jsonFirst = await MeasureAsync(json, workload, portfolio, cutoff, jsonDiagnostics, jsonObservation);
            }
            else
            {
                jsonFirst = await MeasureAsync(json, workload, portfolio, cutoff, jsonDiagnostics, jsonObservation);
                scalarFirst = await MeasureAsync(scalar, workload, portfolio, cutoff, scalarDiagnostics, scalarObservation);
            }
            ValidatePair(scalarFirst, jsonFirst);
            await trace.WriteLineAsync(JsonSerializer.Serialize(new
            {
                RunId = runId, Scenario = workload.Scenario.Name, Phase = "warmup",
                slot.Sample, slot.Block, Portfolio = portfolio, slot.ModeOrder, Cutoff = cutoff
            }));
        }

        for (int i = 0; i < samples; i++)
        {
            if (runClock.Elapsed > MaximumDuration || traceFile.Position > MaximumTraceBytes)
            {
                throw new InvalidOperationException("The feasibility runtime or artifact-size limit was exceeded.");
            }
            CacheScheduleSlot slot = CacheFeasibilitySchedule.At(i);
            int sampleNumber = slot.Sample;
            if (workload.Scenario.WriteEvery > 0 && sampleNumber % workload.Scenario.WriteEvery == 0)
            {
                Guid instrument = await workload.WriteAsync(scalar, sampleNumber);
                oracle.Invalidate(instrument);
                await trace.WriteLineAsync(JsonSerializer.Serialize(new
                {
                    RunId = runId, Scenario = workload.Scenario.Name, Phase = "write",
                    Sample = sampleNumber, slot.Block, WriteInstrument = instrument
                }));
            }
            DateTimeOffset cutoff = Cutoff(workload.Scenario);
            Guid portfolio = slot.PortfolioA ? workload.PortfolioA : workload.PortfolioB;
            CacheMeasurement scalarResult;
            CacheMeasurement jsonResult;
            if (!slot.JsonFirst)
            {
                scalarResult = await MeasureAsync(scalar, workload, portfolio, cutoff, scalarDiagnostics, scalarObservation);
                jsonResult = await MeasureAsync(json, workload, portfolio, cutoff, jsonDiagnostics, jsonObservation);
            }
            else
            {
                jsonResult = await MeasureAsync(json, workload, portfolio, cutoff, jsonDiagnostics, jsonObservation);
                scalarResult = await MeasureAsync(scalar, workload, portfolio, cutoff, scalarDiagnostics, scalarObservation);
            }
            ValidatePair(scalarResult, jsonResult);
            oracle.Observe(cutoff, scalarResult.Requested, scalarResult.Selected, workload.Select);
            scalarSamples.Add(scalarResult);
            jsonSamples.Add(jsonResult);
            await trace.WriteLineAsync(JsonSerializer.Serialize(new
            {
                RunId = runId, Scenario = workload.Scenario.Name, Phase = "measured",
                Sample = sampleNumber, slot.Block, Portfolio = portfolio, slot.ModeOrder, Cutoff = cutoff,
                Scalar = new { scalarResult.ElapsedMs, scalarResult.PriceMs, scalarResult.PriceReaderMs, scalarResult.ApproxAllocatedBytes },
                Json = new { jsonResult.ElapsedMs, jsonResult.PriceMs, jsonResult.PriceReaderMs, jsonResult.ApproxAllocatedBytes },
                Requested = scalarResult.Requested,
                Selected = scalarResult.Selected.Select(price => new { price.InstrumentId, price.Id })
            }));
            await trace.FlushAsync();
            if (traceFile.Position > MaximumTraceBytes)
            {
                throw new InvalidOperationException("The feasibility artifact-size limit was exceeded.");
            }
        }
        await trace.FlushAsync();
        return new CacheScenarioResult(workload.Scenario, samples, warmup,
            new CacheScheduleMetadata(CacheFeasibilitySchedule.Version, workload.PortfolioA, workload.PortfolioB,
                CacheFeasibilitySchedule.Count(warmup), CacheFeasibilitySchedule.Count(samples)),
            Summarize(scalarFirstMs, scalarSamples), Summarize(jsonFirstMs, jsonSamples),
            new CacheOracleResult(oracle.Requested, oracle.Distinct, oracle.RepeatedSelection,
                oracle.ExactHits, oracle.IntervalHits, oracle.Batches, oracle.ExactFullBatches,
                oracle.ExactPartialBatches, oracle.IntervalFullBatches, oracle.IntervalPartialBatches, oracle.Writes));
    }

    private static DateTimeOffset Cutoff(CacheScenario scenario) =>
        scenario.CurrentCutoff ? DateTimeOffset.UtcNow : CacheFeasibilityWorkload.HistoricalCutoff;

    private static async Task<CacheMeasurement> MeasureAsync(
        ServiceProvider provider, CacheFeasibilityWorkload workload, Guid portfolio,
        DateTimeOffset cutoff, ValuationDiagnosticsCollector diagnostics, CacheFeasibilityObservation observation)
    {
        diagnostics.Reset();
        observation.Reset();
        long allocated = GC.GetTotalAllocatedBytes(precise: false);
        long started = Stopwatch.GetTimestamp();
        PortfolioValuation valuation;
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            valuation = await scope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>()
                .GetPortfolioValuationAsync(portfolio, cutoff, CancellationToken.None);
        }
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocation = Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - allocated);
        workload.Validate(valuation, portfolio, cutoff);
        ValuationDiagnosticStage stage = diagnostics.Stages.Single(
            item => item.StageName == "market-price.retrieval-and-materialization");
        if (observation.Calls != 1 || observation.Requested.Length != workload.Scenario.Count ||
            observation.Selected.Length != workload.Scenario.Count ||
            stage.ReaderCommandCount != (observation.Requested.Distinct().Count() + 1_999) / 2_000 ||
            !double.IsFinite(elapsed) || !double.IsFinite(stage.ElapsedMilliseconds))
        {
            throw new InvalidOperationException("The measured price stage or observed batch shape is invalid.");
        }
        return new CacheMeasurement(elapsed, stage.ElapsedMilliseconds, stage.ReaderExecuteMilliseconds,
            allocation, observation.Requested, observation.Selected);
    }

    private static void ValidatePair(CacheMeasurement scalar, CacheMeasurement json)
    {
        Dictionary<Guid, MarketPrice> first = scalar.Selected.ToDictionary(price => price.InstrumentId);
        if (!scalar.Requested.SequenceEqual(json.Requested) ||
            json.Selected.Any(price => !first.TryGetValue(price.InstrumentId, out MarketPrice? other) ||
                                       other.Id != price.Id || other.Price != price.Price ||
                                       other.AsOfUtc != price.AsOfUtc))
        {
            throw new InvalidOperationException("Scalar and JSON selected different prices or instrument sets.");
        }
    }

    private static CacheModeResult Summarize(double first, List<CacheMeasurement> samples)
    {
        double[] durations = samples.Select(item => item.ElapsedMs).Order().ToArray();
        double Percentile(double p) => durations[Math.Max(0, (int)Math.Ceiling(p * durations.Length) - 1)];
        return new CacheModeResult(first, Percentile(.5), Percentile(.95), Percentile(.99),
            samples.Average(item => item.ElapsedMs), samples.Average(item => item.PriceMs),
            samples.Average(item => item.PriceReaderMs), samples.Average(item => (double)item.ApproxAllocatedBytes),
            samples.Count / (samples.Sum(item => item.ElapsedMs) / 1_000d));
    }
}

internal sealed record CacheMeasurement(double ElapsedMs, double PriceMs, double PriceReaderMs,
    long ApproxAllocatedBytes, Guid[] Requested, MarketPrice[] Selected);
internal sealed record CacheModeResult(double FirstClientMs, double P50Ms, double P95Ms, double P99Ms,
    double MeanMs, double PriceMeanMs, double PriceReaderMeanMs, double ApproxAllocatedBytes, double CallsPerSecond);
internal sealed record CacheOracleResult(long Requested, long Distinct, long RepeatedSelection, long ExactHits,
    long IntervalHits, long Batches, long ExactFullBatches, long ExactPartialBatches,
    long IntervalFullBatches, long IntervalPartialBatches, int Writes);
internal sealed record CacheScheduleMetadata(string Version, Guid PortfolioA, Guid PortfolioB,
    CacheScheduleCounts Warmup, CacheScheduleCounts Measured);
internal sealed record CacheScenarioResult(CacheScenario Scenario, int Samples, int Warmup,
    CacheScheduleMetadata Schedule, CacheModeResult Scalar, CacheModeResult Json, CacheOracleResult Oracle);
