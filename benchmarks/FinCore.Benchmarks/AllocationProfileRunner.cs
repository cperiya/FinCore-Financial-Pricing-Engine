using System.Diagnostics;
using System.Text.Json;
using FinCore.Application.Portfolios;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class AllocationProfileRunner
{
    public static async Task RunAsync(
        int instrumentCount,
        int sampleCount,
        int warmupCount,
        Guid sessionId)
    {
        SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
        await using BenchmarkDatabaseFixture database = new();
        await database.InitializeAsync();
        await SyntheticPortfolioWorkload.SeedAsync(database.BenchmarkConnectionString, workload);

        SqlCommandDiagnosticsInterceptor sqlDiagnostics = new();
        ValuationDiagnosticsCollector collector = new(sqlDiagnostics);
        await using ServiceProvider plainProvider =
            BenchmarkServiceProviderFactory.Create(database.BenchmarkConnectionString);
        await using ServiceProvider instrumentedProvider =
            BenchmarkServiceProviderFactory.Create(
                database.BenchmarkConnectionString,
                sqlDiagnostics,
                collector);

        await VerifyAsync(plainProvider, workload);
        await VerifyAsync(instrumentedProvider, workload);

        for (int index = 0; index < warmupCount; index++)
        {
            await VerifyAsync(plainProvider, workload);
            collector.Reset();
            await VerifyAsync(instrumentedProvider, workload);
        }

        Console.WriteLine($"Database: {database.DatabaseName}");
        Console.WriteLine($"Process ID: {Environment.ProcessId}");
        Console.WriteLine($"Instrument count: {instrumentCount}");
        Console.WriteLine($"Warmups per mode: {warmupCount}");
        Console.WriteLine($"Measured calls: {sampleCount}");

        string artifactsDirectory = GetArtifactsDirectory();
        string sessionToken = sessionId.ToString("N");
        string readyPath = Path.Combine(artifactsDirectory, $"profile-{sessionToken}.ready");
        string goPath = Path.Combine(artifactsDirectory, $"profile-{sessionToken}.go");
        if (File.Exists(readyPath) || File.Exists(goPath))
        {
            throw new InvalidOperationException(
                "Allocation profiling handshake files already exist; refusing to overwrite them.");
        }

        try
        {
            await File.WriteAllTextAsync(
                readyPath,
                $"{database.DatabaseName};{Environment.ProcessId};{instrumentCount}");
            Console.WriteLine("Ready for the attached allocation trace.");
            await WaitForProfilerSignalAsync(goPath, TimeSpan.FromSeconds(60));

            ProfilePhaseResult plainPhase = await MeasurePhaseAsync(
                plainProvider,
                collector: null,
                workload,
                instrumented: false,
                sampleCount);
            ProfilePhaseResult instrumentedPhase = await MeasurePhaseAsync(
                instrumentedProvider,
                collector,
                workload,
                instrumented: true,
                sampleCount);

            string phaseReportPath = Path.Combine(
                artifactsDirectory,
                $"allocation-phases-{instrumentCount}-{sessionToken}.json");
            string phaseReport = JsonSerializer.Serialize(
                new AllocationPhaseReport(
                    database.DatabaseName,
                    instrumentCount,
                    warmupCount,
                    sampleCount,
                    DateTimeOffset.UtcNow,
                    plainPhase,
                    instrumentedPhase),
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(phaseReportPath, phaseReport);
            Console.WriteLine($"Phase report: {phaseReportPath}");
            Console.WriteLine(
                $"No diagnostics: {plainPhase.ElapsedMilliseconds:F3} ms; " +
                $"with diagnostics/interceptor: {instrumentedPhase.ElapsedMilliseconds:F3} ms.");
        }
        finally
        {
            if (File.Exists(goPath))
            {
                File.Delete(goPath);
            }

            if (File.Exists(readyPath))
            {
                File.Delete(readyPath);
            }
        }
    }

    private static async Task<ProfilePhaseResult> MeasurePhaseAsync(
        ServiceProvider serviceProvider,
        ValuationDiagnosticsCollector? collector,
        SyntheticWorkload workload,
        bool instrumented,
        int sampleCount)
    {
        DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
        Stopwatch elapsed = Stopwatch.StartNew();
        for (int index = 0; index < sampleCount; index++)
        {
            if (instrumented)
            {
                collector?.Reset();
            }

            await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
        }

        elapsed.Stop();
        return new ProfilePhaseResult(
            instrumented ? "stage-diagnostics-and-sql-interceptor" : "no-application-diagnostics",
            startedAtUtc,
            DateTimeOffset.UtcNow,
            sampleCount,
            elapsed.Elapsed.TotalMilliseconds);
    }

    private static async Task VerifyAsync(
        ServiceProvider serviceProvider,
        SyntheticWorkload workload)
    {
        FinCore.Application.Portfolios.PortfolioValuation valuation =
            await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
        SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
    }

    internal sealed record ProfilePhaseResult(
        string Mode,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        int SampleCount,
        double ElapsedMilliseconds);

    internal sealed record AllocationPhaseReport(
        string DatabaseName,
        int InstrumentCount,
        int WarmupCallsPerMode,
        int SampleCountPerMode,
        DateTimeOffset CapturedAtUtc,
        ProfilePhaseResult Plain,
        ProfilePhaseResult Instrumented);

    private static async Task WaitForProfilerSignalAsync(string signalPath, TimeSpan timeout)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!File.Exists(signalPath))
        {
            if (timer.Elapsed >= timeout)
            {
                throw new TimeoutException(
                    "The profiler did not attach within 60 seconds; benchmark database cleanup will run.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }
    }

    private static string GetArtifactsDirectory()
    {
        DirectoryInfo? directory = new(Path.GetFullPath(Environment.CurrentDirectory));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinCore.slnx")))
            {
                string path = Path.Combine(
                    directory.FullName,
                    "BenchmarkDotNet.Artifacts",
                    "AllocationTraces");
                Directory.CreateDirectory(path);
                return path;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate FinCore.slnx; profiler handshake was not created.");
    }
}
