using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace FinCore.Benchmarks;

internal static class BenchmarkRunValidation
{
    internal static void Run(
        Func<Summary[]> execute,
        Type benchmarkType,
        IReadOnlyCollection<int> instrumentCounts,
        int launchCount,
        string artifactDirectory)
    {
        string incompletePath = Path.Combine(artifactDirectory, "INCOMPLETE.txt");
        string failedPath = Path.Combine(artifactDirectory, "FAILED.txt");
        Directory.CreateDirectory(artifactDirectory);
        File.WriteAllText(incompletePath, "INCOMPLETE: benchmark results have not been validated.");

        try
        {
            Validate(execute(), benchmarkType, instrumentCounts, launchCount);
            File.Delete(incompletePath);
        }
        catch
        {
            File.WriteAllText(
                failedPath,
                "FAILED: benchmark results must not be used as a successful run. Inspect the local BenchmarkDotNet log.");
            Console.Error.WriteLine($"FAILED: benchmark run; diagnostic artifacts are in {artifactDirectory}");
            throw;
        }
    }

    internal static void Validate(
        IReadOnlyCollection<Summary> summaries,
        Type benchmarkType,
        IReadOnlyCollection<int> instrumentCounts,
        int launchCount)
    {
        HashSet<(int InstrumentCount, bool UseJson)> expected =
            instrumentCounts.SelectMany(count => new[] { (count, false), (count, true) }).ToHashSet();
        HashSet<(int InstrumentCount, bool UseJson)> seen = [];

        if (summaries.Count == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors))
        {
            throw new InvalidOperationException("FAILED: BenchmarkDotNet returned no valid summaries.");
        }

        foreach (BenchmarkReport report in summaries.SelectMany(summary => summary.Reports))
        {
            BenchmarkCase benchmark = report.BenchmarkCase;
            if (benchmark.Descriptor.Type != benchmarkType ||
                benchmark.Descriptor.WorkloadMethod.Name != "ValuePortfolioAsync" ||
                benchmark.Parameters.Count != 2 ||
                benchmark.Parameters["InstrumentCount"] is not int count ||
                benchmark.Parameters["UseJsonParameter"] is not bool useJson ||
                !expected.Contains((count, useJson)) ||
                !seen.Add((count, useJson)))
            {
                throw new InvalidOperationException($"FAILED: unexpected or duplicate benchmark case: {benchmark.DisplayInfo}");
            }

            ValidateReport(report, launchCount);
        }

        if (!seen.SetEquals(expected))
        {
            throw new InvalidOperationException(
                $"FAILED: benchmark case matrix incomplete; expected {expected.Count}, received {seen.Count}.");
        }
    }

    internal static void ValidateBaseline(
        IReadOnlyCollection<Summary> summaries,
        IReadOnlyCollection<BenchmarkCase> expectedCases)
    {
        if (summaries.Count == 0 || summaries.Any(summary => summary.HasCriticalValidationErrors))
        {
            throw new InvalidOperationException("FAILED: BenchmarkDotNet returned no valid summaries.");
        }

        Dictionary<string, BenchmarkCase> expected = expectedCases.ToDictionary(item => item.FolderInfo);
        HashSet<string> seen = [];
        foreach (BenchmarkReport report in summaries.SelectMany(summary => summary.Reports))
        {
            string key = report.BenchmarkCase.FolderInfo;
            if (!expected.TryGetValue(key, out BenchmarkCase? benchmark) || !seen.Add(key))
            {
                throw new InvalidOperationException("FAILED: unexpected or duplicate baseline benchmark case.");
            }

            int? launchCount = benchmark.Job.HasValue(RunMode.LaunchCountCharacteristic)
                ? benchmark.Job.Run.LaunchCount
                : null;
            ValidateReport(report, launchCount);
        }

        if (expected.Count == 0 || seen.Count != expected.Count)
        {
            throw new InvalidOperationException(
                $"FAILED: benchmark case matrix incomplete; expected {expected.Count}, received {seen.Count}.");
        }
    }

    private static void ValidateReport(BenchmarkReport report, int? launchCount)
    {
        if (!report.Success || !report.BuildResult.IsBuildSuccess ||
            report.ExecuteResults.Count == 0 ||
            (launchCount.HasValue && report.ExecuteResults.Count != launchCount.Value) ||
            report.ExecuteResults.Any(result =>
                !result.FoundExecutable || result.ExitCode != 0 || !result.IsSuccess ||
                result.Errors.Count != 0 ||
                !result.Measurements.Any(measurement =>
                    measurement.IterationMode == IterationMode.Workload &&
                    measurement.IterationStage == IterationStage.Result) ||
                result.Measurements.Any(measurement =>
                    measurement.IterationMode == IterationMode.Workload &&
                    measurement.IterationStage == IterationStage.Result &&
                    (measurement.Operations <= 0 ||
                     !double.IsFinite(measurement.Nanoseconds) || measurement.Nanoseconds < 0))) ||
            report.ResultStatistics is not { } statistics ||
            !double.IsFinite(statistics.Mean) ||
            !double.IsFinite(statistics.Median) ||
            !double.IsFinite(statistics.StandardDeviation) ||
            !report.Metrics.TryGetValue("Allocated Memory", out Metric? allocation) ||
            !double.IsFinite(allocation.Value) || allocation.Value < 0)
        {
            throw new InvalidOperationException("FAILED: incomplete or invalid benchmark case.");
        }
    }
}
