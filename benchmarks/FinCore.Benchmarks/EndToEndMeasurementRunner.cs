using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FinCore.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class EndToEndMeasurementRunner
{
    public static async Task RunAsync(MeasurementOptions options)
    {
        await using BenchmarkDatabaseFixture database = new();
        await database.InitializeAsync();

        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            await SyntheticPortfolioWorkload.SeedAsync(
                database.BenchmarkConnectionString,
                SyntheticPortfolioWorkload.Create(instrumentCount));
        }

        await using ServiceProvider serviceProvider =
            BenchmarkServiceProviderFactory.Create(database.BenchmarkConnectionString);

        List<WorkloadMeasurement> results = [];
        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            (double coldMilliseconds, FinCore.Application.Portfolios.PortfolioValuation coldValuation) =
                await MeasureOneAsync(serviceProvider, workload);
            SyntheticPortfolioWorkload.ValidateValuation(workload, coldValuation);

            for (int index = 0; index < options.Warmup; index++)
            {
                FinCore.Application.Portfolios.PortfolioValuation warmupValuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, warmupValuation);
            }

            List<double> samplesMilliseconds = new(options.Samples);
            double summedCallMilliseconds = 0;
            for (int index = 0; index < options.Samples; index++)
            {
                (double elapsedMilliseconds, FinCore.Application.Portfolios.PortfolioValuation valuation) =
                    await MeasureOneAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
                samplesMilliseconds.Add(elapsedMilliseconds);
                summedCallMilliseconds += elapsedMilliseconds;
            }

            results.Add(new WorkloadMeasurement(
                instrumentCount,
                workload.ExpectedTotal,
                coldMilliseconds,
                options.Warmup,
                options.Samples,
                Percentile(samplesMilliseconds, 0.50),
                Percentile(samplesMilliseconds, 0.95),
                Percentile(samplesMilliseconds, 0.99),
                summedCallMilliseconds,
                options.Samples / (summedCallMilliseconds / 1_000d)));
        }

        BenchmarkEnvironment environment = await BenchmarkEnvironment.CaptureAsync(database.ServerEnvironment);
        string resultsPath = WriteResults(environment, options, database.DatabaseName, results);

        Console.WriteLine($"Database: {database.DatabaseName}");
        Console.WriteLine($"Run type: Release, warm SQL Server process; first valuation per workload reported separately.");
        Console.WriteLine($"Environment: {environment.ToSummary()}");
        Console.WriteLine($"Results file: {resultsPath}");
        Console.WriteLine("Sequential valuation latency includes a fresh DI scope and SQL-backed service call.");
        Console.WriteLine("Throughput is samples divided by the sum of measured call durations.");
        Console.WriteLine("Workload  Cold-first(ms)  Warmup  Samples  p50(ms)  p95(ms)  p99(ms)  calls/sec");
        foreach (WorkloadMeasurement result in results)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,8}  {1,14:F3}  {2,6}  {3,7}  {4,7:F3}  {5,7:F3}  {6,7:F3}  {7,9:F2}",
                result.InstrumentCount,
                result.ColdFirstCallMilliseconds,
                result.WarmupCalls,
                result.SampleCount,
                result.P50Milliseconds,
                result.P95Milliseconds,
                result.P99Milliseconds,
                result.SequentialCallsPerSecond));
        }

        Console.WriteLine("No HTTP host startup, database provisioning, migration, or seeding is included in timed calls.");
    }

    public static async Task RunDiagnosticsAsync(MeasurementOptions options)
    {
        await using BenchmarkDatabaseFixture database = new();
        await database.InitializeAsync();

        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            await SyntheticPortfolioWorkload.SeedAsync(
                database.BenchmarkConnectionString,
                SyntheticPortfolioWorkload.Create(instrumentCount));
        }

        SqlCommandDiagnosticsInterceptor interceptor = new();
        await using ServiceProvider serviceProvider =
            BenchmarkServiceProviderFactory.Create(database.BenchmarkConnectionString, interceptor);

        Console.WriteLine($"Database: {database.DatabaseName}");
        Console.WriteLine(
            $"Diagnostics only: {options.Samples} calls per workload; no latency percentiles are reported.");
        Console.WriteLine("Workload  Calls  Reader commands/call  Reader execute duration/call(ms)");

        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            FinCore.Application.Portfolios.PortfolioValuation setupValuation =
                await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
            SyntheticPortfolioWorkload.ValidateValuation(workload, setupValuation);
            for (int index = 0; index < options.Warmup; index++)
            {
                FinCore.Application.Portfolios.PortfolioValuation warmupValuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, warmupValuation);
            }

            interceptor.Reset();

            for (int index = 0; index < options.Samples; index++)
            {
                FinCore.Application.Portfolios.PortfolioValuation valuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
            }

            SqlCommandDiagnosticsSnapshot snapshot = interceptor.Snapshot();
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,8}  {1,5}  {2,20:F2}  {3,32:F3}",
                instrumentCount,
                options.Samples,
                snapshot.ReaderCommandCount / (double)options.Samples,
                snapshot.ReaderExecuteDuration.TotalMilliseconds / options.Samples));
        }

        Console.WriteLine(
            "Reader execute duration ends when SQL Server returns a data reader; it excludes EF materialization.");
    }

    public static async Task RunStageDiagnosticsAsync(MeasurementOptions options)
    {
        await using BenchmarkDatabaseFixture database = new();
        await database.InitializeAsync();

        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            await SyntheticPortfolioWorkload.SeedAsync(
                database.BenchmarkConnectionString,
                SyntheticPortfolioWorkload.Create(instrumentCount));
        }

        SqlCommandDiagnosticsInterceptor sqlDiagnostics = new();
        ValuationDiagnosticsCollector collector = new(sqlDiagnostics);
        await using ServiceProvider serviceProvider =
            BenchmarkServiceProviderFactory.Create(
                database.BenchmarkConnectionString,
                sqlDiagnostics,
                collector);

        List<StageDiagnosticMeasurement> measurements = [];
        Console.WriteLine($"Database: {database.DatabaseName}");
        Console.WriteLine(
            $"Stage diagnostics: {options.Samples} measured calls per workload; " +
            "managed allocation values are approximate process-wide deltas.");

        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            FinCore.Application.Portfolios.PortfolioValuation setupValuation =
                await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
            SyntheticPortfolioWorkload.ValidateValuation(workload, setupValuation);

            for (int index = 0; index < options.Warmup; index++)
            {
                FinCore.Application.Portfolios.PortfolioValuation warmupValuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, warmupValuation);
            }

            List<ValuationDiagnosticStage> stages = [];
            for (int index = 0; index < options.Samples; index++)
            {
                collector.Reset();
                FinCore.Application.Portfolios.PortfolioValuation valuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
                stages.AddRange(collector.Stages);
            }

            IReadOnlyList<StageDiagnosticMeasurement> workloadMeasurements =
                AggregateStageMeasurements(instrumentCount, options.Samples, stages);
            measurements.AddRange(workloadMeasurements);

            Console.WriteLine($"Workload: {instrumentCount} instruments");
            Console.WriteLine(
                "Stage                                             ms/call  % total  alloc KB  " +
                "in/out  SQL cmds  SQL ms");
            foreach (StageDiagnosticMeasurement measurement in workloadMeasurements)
            {
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-48} {1,8:F3} {2,7:F1}% {3,9:F1} {4,4}/{5,-4} {6,8:F2} {7,8:F3}",
                    measurement.StageName,
                    measurement.AverageElapsedMilliseconds,
                    measurement.PercentOfTransactionDuration,
                    measurement.AverageApproximateAllocationsBytes / 1024d,
                    measurement.AverageInputCount,
                    measurement.AverageOutputCount,
                    measurement.AverageReaderCommands,
                    measurement.AverageReaderMilliseconds));
            }
        }

        BenchmarkEnvironment environment = await BenchmarkEnvironment.CaptureAsync(database.ServerEnvironment);
        IReadOnlyList<SqlPlanSnapshot> plans =
            await SqlPlanCapture.CaptureExecutedPlansAsync(
                database.BenchmarkConnectionString,
                sqlDiagnostics.Commands);
        string resultsPath = WriteStageDiagnosticResults(
            environment,
            options,
            database.DatabaseName,
            measurements,
            plans);
        Console.WriteLine($"Stage diagnostic results file: {resultsPath}");
        Console.WriteLine($"Captured {plans.Count} cached SQL execution plans before cleanup.");
        Console.WriteLine(
            "Stage allocations are approximate process-wide deltas and must not be summed " +
            "as authoritative per-stage ownership. BenchmarkDotNet remains authoritative " +
            "for allocation-per-operation totals.");
    }

    private static IReadOnlyList<StageDiagnosticMeasurement> AggregateStageMeasurements(
        int instrumentCount,
        int sampleCount,
        IReadOnlyList<ValuationDiagnosticStage> stages)
    {
        double transactionMilliseconds = stages
            .Where(stage => stage.StageName == "transaction.valuation")
            .Sum(stage => stage.ElapsedMilliseconds) / sampleCount;

        return stages
            .GroupBy(stage => stage.StageName, StringComparer.Ordinal)
            .Select(group => new StageDiagnosticMeasurement(
                instrumentCount,
                group.Key,
                sampleCount,
                group.Average(stage => stage.ElapsedMilliseconds),
                transactionMilliseconds <= 0
                    ? 0
                    : group.Average(stage => stage.ElapsedMilliseconds) /
                        transactionMilliseconds * 100,
                group.Average(stage => stage.ApproximateManagedAllocationsBytes),
                group.Average(stage => stage.InputCount),
                group.Average(stage => stage.OutputCount),
                group.Average(stage => stage.ReaderCommandCount),
                group.Average(stage => stage.ReaderExecuteMilliseconds)))
            .OrderByDescending(measurement => measurement.StageName == "transaction.valuation")
            .ThenByDescending(measurement => measurement.AverageElapsedMilliseconds)
            .ToArray();
    }

    private static async Task<(double ElapsedMilliseconds, FinCore.Application.Portfolios.PortfolioValuation Valuation)>
        MeasureOneAsync(ServiceProvider serviceProvider, SyntheticWorkload workload)
    {
        long startedAt = Stopwatch.GetTimestamp();
        FinCore.Application.Portfolios.PortfolioValuation valuation =
            await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
        double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        return (elapsedMilliseconds, valuation);
    }

    private static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        double[] ordered = values.Order().ToArray();
        int nearestRank = Math.Max(1, (int)Math.Ceiling(percentile * ordered.Length));
        return ordered[nearestRank - 1];
    }

    private static string WriteResults(
        BenchmarkEnvironment environment,
        MeasurementOptions options,
        string databaseName,
        IReadOnlyList<WorkloadMeasurement> results)
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "BenchmarkDotNet.Artifacts", "EndToEndReports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"portfolio-valuation-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
        string json = JsonSerializer.Serialize(
            new EndToEndReport(
                databaseName,
                DateTimeOffset.UtcNow,
                environment,
                options,
                results),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    private static string WriteStageDiagnosticResults(
        BenchmarkEnvironment environment,
        MeasurementOptions options,
        string databaseName,
        IReadOnlyList<StageDiagnosticMeasurement> measurements,
        IReadOnlyList<SqlPlanSnapshot> plans)
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(root, "BenchmarkDotNet.Artifacts", "StageDiagnosticReports");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"portfolio-valuation-stages-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json");
        string json = JsonSerializer.Serialize(
            new StageDiagnosticReport(
                databaseName,
                DateTimeOffset.UtcNow,
                environment,
                options,
                measurements,
                plans),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        foreach (string startPath in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new(Path.GetFullPath(startPath));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "FinCore.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate FinCore.slnx; benchmark results were not written.");
    }
}

internal sealed record WorkloadMeasurement(
    int InstrumentCount,
    decimal ExpectedTotalUsd,
    double ColdFirstCallMilliseconds,
    int WarmupCalls,
    int SampleCount,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double SummedCallMilliseconds,
    double SequentialCallsPerSecond);

internal sealed record EndToEndReport(
    string DatabaseName,
    DateTimeOffset CapturedAtUtc,
    BenchmarkEnvironment Environment,
    MeasurementOptions MeasurementOptions,
    IReadOnlyList<WorkloadMeasurement> Workloads);

internal sealed record StageDiagnosticMeasurement(
    int InstrumentCount,
    string StageName,
    int SampleCount,
    double AverageElapsedMilliseconds,
    double PercentOfTransactionDuration,
    double AverageApproximateAllocationsBytes,
    double AverageInputCount,
    double AverageOutputCount,
    double AverageReaderCommands,
    double AverageReaderMilliseconds);

internal sealed record StageDiagnosticReport(
    string DatabaseName,
    DateTimeOffset CapturedAtUtc,
    BenchmarkEnvironment Environment,
    MeasurementOptions MeasurementOptions,
    IReadOnlyList<StageDiagnosticMeasurement> Measurements,
    IReadOnlyList<SqlPlanSnapshot> SqlPlans);

internal sealed record BenchmarkEnvironment(
    string Commit,
    bool WorkingTreeDirty,
    string BuildConfiguration,
    string OperatingSystem,
    string Runtime,
    string Architecture,
    string ProcessorIdentifier,
    int LogicalProcessorCount,
    ulong? InstalledMemoryBytes,
    string SqlServerVersion)
{
    public string ToSummary()
    {
        string memory = InstalledMemoryBytes is ulong bytes
            ? $"{bytes / (1024d * 1024d * 1024d):F1} GiB RAM"
            : "RAM unavailable";

        return $"{OperatingSystem}; {Runtime}; {ProcessorIdentifier}; " +
               $"{LogicalProcessorCount} logical processors; {memory}; {SqlServerVersion}; " +
               $"{BuildConfiguration}; commit {Commit}; working tree dirty={WorkingTreeDirty}";
    }

    public static async Task<BenchmarkEnvironment> CaptureAsync(SqlServerEnvironment sqlServer)
    {
        (string commit, bool isDirty) = await GetGitStateAsync();
        string processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "not reported";
        ulong? installedMemory = System.OperatingSystem.IsWindows()
            ? WindowsMemoryStatus.GetInstalledMemoryBytes()
            : null;

        return new BenchmarkEnvironment(
            commit,
            isDirty,
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            processor,
            Environment.ProcessorCount,
            installedMemory,
            sqlServer.ToSummary());
    }

    private static async Task<(string Commit, bool IsDirty)> GetGitStateAsync()
    {
        string? commitOutput = await RunGitAsync("rev-parse --short HEAD");
        if (commitOutput is null)
        {
            return ("unavailable", true);
        }

        string? status = await RunGitAsync("status --porcelain");
        return (commitOutput.Trim(), status is null || status.Length > 0);
    }

    private static async Task<string?> RunGitAsync(string arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using Process process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not inspect the benchmark repository state.");
        string output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode == 0 ? output : null;
    }
}

internal static class WindowsMemoryStatus
{
    public static ulong? GetInstalledMemoryBytes()
    {
        if (!OperatingSystem.IsWindows() ||
            !GetPhysicallyInstalledSystemMemory(out ulong kilobytes))
        {
            return null;
        }

        return checked(kilobytes * 1_024);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);
}
