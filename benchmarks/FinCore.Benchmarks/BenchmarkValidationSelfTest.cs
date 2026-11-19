using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.Results;

namespace FinCore.Benchmarks;

internal static class BenchmarkValidationSelfTest
{
    private const string FailureEnvironmentVariable = "FINCORE_BENCHMARK_SELF_TEST_FAILURE";

    internal static void Run()
    {
        string directory = Path.Combine(
            Environment.CurrentDirectory,
            "BenchmarkDotNet.Artifacts",
            "HarnessSelfTest",
            $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string successDirectory = Path.Combine(directory, "success");
        string failureDirectory = Path.Combine(directory, "failure");
        Summary[] successful = [];
        Summary[] failed = [];
        string? previous = Environment.GetEnvironmentVariable(FailureEnvironmentVariable);

        try
        {
            Environment.SetEnvironmentVariable(FailureEnvironmentVariable, null);
            BenchmarkRunValidation.Run(
                () => successful = Execute(successDirectory),
                typeof(BenchmarkValidationSmokeBenchmarks), [1], 1, successDirectory);
            if (File.Exists(Path.Combine(successDirectory, "INCOMPLETE.txt")) ||
                File.Exists(Path.Combine(successDirectory, "FAILED.txt")))
            {
                throw new InvalidOperationException("The successful smoke run retained a failure marker.");
            }

            ExpectFailure(
                () => BenchmarkRunValidation.Validate(successful, typeof(BenchmarkValidationSmokeBenchmarks), [1, 2], 1),
                "missing case");
            ExpectFailure(
                () => BenchmarkRunValidation.Validate([], typeof(BenchmarkValidationSmokeBenchmarks), [1], 1),
                "missing summary");
            ExpectFailure(
                () => BenchmarkRunValidation.Validate(successful, typeof(BenchmarkValidationSmokeBenchmarks), [1], 2),
                "missing worker launch");
            ExpectFailure(
                () => BenchmarkRunValidation.Validate(
                    successful.Concat(successful).ToArray(), typeof(BenchmarkValidationSmokeBenchmarks), [1], 1),
                "duplicate case");

            BenchmarkReport original = successful.Single().Reports[0];
            ExecuteResult execution = original.ExecuteResults.Single();
            ExecuteResult nonzero = new(
                execution.FoundExecutable, 17, execution.ProcessId,
                execution.Results, execution.PrefixedLines, execution.StandardOutput, 1);
            BenchmarkReport nonzeroReport = new(
                true, original.BenchmarkCase, original.GenerateResult, original.BuildResult,
                [nonzero], original.Metrics.Values.ToArray());
            Summary modified = CopyWithReplacement(successful.Single(), original, nonzeroReport);
            ExpectFailure(
                () => BenchmarkRunValidation.Validate([modified], typeof(BenchmarkValidationSmokeBenchmarks), [1], 1),
                "nonzero worker exit with measurement results");

            BenchmarkReport noAllocation = new(
                true, original.BenchmarkCase, original.GenerateResult, original.BuildResult,
                original.ExecuteResults, []);
            Summary missingAllocation = CopyWithReplacement(successful.Single(), original, noAllocation);
            ExpectFailure(
                () => BenchmarkRunValidation.Validate([missingAllocation], typeof(BenchmarkValidationSmokeBenchmarks), [1], 1),
                "missing allocation result");
            BenchmarkCase[] expected = successful.Single().Reports.Select(report => report.BenchmarkCase).ToArray();
            Summary missingCase = CopyWithReports(successful.Single(), successful.Single().Reports.Skip(1));
            ExpectFailure(
                () => BenchmarkRunValidation.ValidateBaseline([missingCase], expected),
                "missing default benchmark case");
            ExpectFailure(
                () => BenchmarkRunValidation.ValidateBaseline([modified], expected),
                "default benchmark nonzero worker exit");
            ExpectFailure(
                () => BenchmarkRunValidation.ValidateBaseline([missingAllocation], expected),
                "default benchmark missing allocation");
            Metric[] invalidMetrics = original.Metrics.Values
                .Select(metric => metric.Descriptor.Id == "Allocated Memory"
                    ? new Metric(metric.Descriptor, double.NaN)
                    : metric)
                .ToArray();
            BenchmarkReport invalidAllocation = new(
                true, original.BenchmarkCase, original.GenerateResult, original.BuildResult,
                original.ExecuteResults, invalidMetrics);
            Summary invalidAllocationSummary = CopyWithReplacement(successful.Single(), original, invalidAllocation);
            ExpectFailure(
                () => BenchmarkRunValidation.ValidateBaseline([invalidAllocationSummary], expected),
                "default benchmark invalid allocation");

            Environment.SetEnvironmentVariable(FailureEnvironmentVariable, "1");
            ExpectFailure(
                () => BenchmarkRunValidation.Run(
                    () => failed = Execute(failureDirectory),
                    typeof(BenchmarkValidationSmokeBenchmarks), [1], 1, failureDirectory),
                "actual failing worker");
            if (!File.Exists(Path.Combine(failureDirectory, "FAILED.txt")) ||
                !File.Exists(Path.Combine(failureDirectory, "INCOMPLETE.txt")) ||
                !failed.SelectMany(summary => summary.Reports).Any(report => !report.Success))
            {
                throw new InvalidOperationException("The failing smoke run did not retain marked diagnostic evidence.");
            }

            VerifyDefaultProcessExit(Path.Combine(directory, "default-success"), shouldFail: false);
            VerifyDefaultProcessExit(Path.Combine(directory, "default-failure"), shouldFail: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FailureEnvironmentVariable, previous);
        }

        Console.WriteLine($"Harness self-test passed. Local diagnostic artifacts: {directory}");
    }

    internal static async Task RunSetupFailureAsync()
    {
        ProtectedDatabaseSnapshot before = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedBefore = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        string? databaseName = null;
        PortfolioValuationBenchmarks benchmark = new()
        {
            InstrumentCount = 5,
            AfterDatabaseInitializedForTest = async database =>
            {
                databaseName = database.DatabaseName;
                IReadOnlyList<string> owned = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
                if (!owned.Contains(databaseName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException("The initialized benchmark database was not found.");
                }

                throw new InvalidOperationException("Intentional post-initialization setup failure.");
            }
        };

        bool rejected = false;
        try
        {
            await benchmark.GlobalSetupAsync();
        }
        catch (InvalidOperationException exception)
            when (exception.Message == "Intentional post-initialization setup failure.")
        {
            rejected = true;
        }

        await ExpectCleanupRefusalAsync("FinCore", "Cleanup accepts only an exact");
        await ExpectCleanupRefusalAsync("FinCoreIntegrationTests", "Cleanup accepts only an exact");
        await ExpectCleanupRefusalAsync($"FinCoreBenchmarkRun_{Guid.NewGuid():N}", "No run manifest exists");

        ProtectedDatabaseSnapshot after = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedAfter = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        if (!rejected || databaseName is null || before != after ||
            !ownedBefore.SequenceEqual(ownedAfter, StringComparer.Ordinal) ||
            ownedAfter.Contains(databaseName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The setup-failure test did not verify safe cleanup.");
        }

        Console.WriteLine("Setup-failure cleanup and unverified-ownership refusals passed.");
    }

    internal static async Task RunOwnedDatabaseFailureAsync()
    {
        ProtectedDatabaseSnapshot before = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedBefore = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        string directory = Path.Combine(
            Environment.CurrentDirectory,
            "BenchmarkDotNet.Artifacts",
            "HarnessSelfTest",
            $"owned-failure-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string? databaseName = null;
        string? previous = Environment.GetEnvironmentVariable(FailureEnvironmentVariable);
        bool rejected = false;

        try
        {
            Environment.SetEnvironmentVariable(FailureEnvironmentVariable, "1");
            try
            {
                await using BenchmarkDatabaseFixture database = new();
                await database.InitializeAsync();
                databaseName = database.DatabaseName;
                BenchmarkRunValidation.Run(
                    () => Execute(directory),
                    typeof(BenchmarkValidationSmokeBenchmarks), [1], 1, directory);
            }
            catch (InvalidOperationException exception)
                when (exception.Message.StartsWith("FAILED:", StringComparison.Ordinal))
            {
                rejected = true;
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(FailureEnvironmentVariable, previous);
        }

        ProtectedDatabaseSnapshot after = await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedAfter = await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        if (!rejected || databaseName is null || before != after ||
            !ownedBefore.SequenceEqual(ownedAfter, StringComparer.Ordinal) ||
            ownedAfter.Contains(databaseName, StringComparer.Ordinal) ||
            !File.Exists(Path.Combine(directory, "FAILED.txt")))
        {
            throw new InvalidOperationException("The failing owned-database harness test did not verify safe cleanup.");
        }

        Console.WriteLine($"Owned-database failure cleanup passed. Diagnostic artifacts: {directory}");
    }

    private static Summary[] Execute(string artifactDirectory) =>
        BenchmarkSwitcher.FromTypes([typeof(BenchmarkValidationSmokeBenchmarks)])
            .Run(
            [
                "--filter", "*BenchmarkValidationSmokeBenchmarks*",
                "--artifacts", artifactDirectory,
                "--job", "dry",
                "--join",
                "--launchCount", "1"
            ]).ToArray();

    private static Summary CopyWithReplacement(
        Summary source,
        BenchmarkReport original,
        BenchmarkReport replacement) =>
        CopyWithReports(source, source.Reports.Select(report => report == original ? replacement : report));

    private static Summary CopyWithReports(Summary source, IEnumerable<BenchmarkReport> reports) =>
        new(
            source.Title,
            reports.ToImmutableArray(),
            source.HostEnvironmentInfo,
            source.ResultsDirectoryPath,
            source.LogFilePath,
            source.TotalTime,
            CultureInfo.InvariantCulture,
            source.ValidationErrors,
            source.ColumnHidingRules,
            source.Style);

    private static void VerifyDefaultProcessExit(string directory, bool shouldFail)
    {
        ProcessStartInfo start = new("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("--harness-default-smoke");
        start.ArgumentList.Add("--filter");
        start.ArgumentList.Add("*BenchmarkValidationSmokeBenchmarks*");
        start.ArgumentList.Add("--job");
        start.ArgumentList.Add("dry");
        start.ArgumentList.Add("--artifacts");
        start.ArgumentList.Add(directory);
        if (shouldFail)
        {
            start.Environment[FailureEnvironmentVariable] = "1";
        }
        else
        {
            start.Environment.Remove(FailureEnvironmentVariable);
        }

        using Process process = Process.Start(start) ??
            throw new InvalidOperationException("Could not start the default benchmark process.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, error);
        bool hasFailureMarker = Directory.Exists(directory) &&
            Directory.EnumerateFiles(directory, "FAILED-*.txt").Any() &&
            Directory.EnumerateFiles(directory, "INCOMPLETE-*.txt").Any();
        if ((process.ExitCode != 0) != shouldFail || hasFailureMarker != shouldFail ||
            (shouldFail && !error.Result.Contains("FAILED:", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Default benchmark process exit/marker check failed; artifacts: {directory}");
        }

        Console.WriteLine($"Default benchmark {(shouldFail ? "failure" : "success")} exit code: {process.ExitCode}.");
    }

    private static async Task ExpectCleanupRefusalAsync(string databaseName, string expectedMessage)
    {
        try
        {
            await BenchmarkDatabaseFixture.CleanupOwnedDatabaseAsync(databaseName);
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"Cleanup unexpectedly accepted unverified database '{databaseName}'.");
    }

    private static void ExpectFailure(Action action, string scenario)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("FAILED:", StringComparison.Ordinal))
        {
            Console.WriteLine($"Rejected {scenario}.");
            return;
        }

        throw new InvalidOperationException($"Harness self-test did not reject {scenario}.");
    }
}

/// <summary>
/// Exercises BenchmarkDotNet success and worker-failure handling without a database.
/// </summary>
[MemoryDiagnoser]
public class BenchmarkValidationSmokeBenchmarks
{
    /// <summary>Gets or sets the synthetic workload size.</summary>
    [Params(1)]
    public int InstrumentCount { get; set; }

    /// <summary>Gets or sets the synthetic translation mode.</summary>
    [Params(false, true)]
    public bool UseJsonParameter { get; set; }

    /// <summary>
    /// Simulates a worker setup failure for one case when the self-test requests it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown for the deliberately failing test case.</exception>
    [GlobalSetup]
    public void Setup()
    {
        if (UseJsonParameter &&
            Environment.GetEnvironmentVariable("FINCORE_BENCHMARK_SELF_TEST_FAILURE") == "1")
        {
            throw new InvalidOperationException("Intentional benchmark harness self-test failure.");
        }
    }

    /// <summary>Executes a small synthetic measurement.</summary>
    /// <returns>A completed task containing an allocated object.</returns>
    [Benchmark]
    public Task<object> ValuePortfolioAsync() => Task.FromResult<object>(new object());
}
