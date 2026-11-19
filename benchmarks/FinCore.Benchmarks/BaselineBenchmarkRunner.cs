using BenchmarkDotNet.Configs;
using BenchmarkDotNet.ConsoleArguments;
using BenchmarkDotNet.ConsoleArguments.ListBenchmarks;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace FinCore.Benchmarks;

internal static class BaselineBenchmarkRunner
{
    internal static int Run(Type benchmarkType, string[] args)
    {
        try
        {
            return RunCore(benchmarkType, args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAILED: baseline benchmark could not start or record its status ({exception.GetType().Name}).");
            return 1;
        }
    }

    private static int RunCore(Type benchmarkType, string[] args)
    {
        BenchmarkSwitcher switcher = BenchmarkSwitcher.FromTypes([benchmarkType]);
        if (args.Length == 1 && args[0] == "--help")
        {
            switcher.Run(args);
            return 0;
        }

        (bool success, IConfig parsedConfig, CommandLineOptions options) =
            ConfigParser.Parse(args, NullLogger.Instance, DefaultConfig.Instance);
        if (!success)
        {
            switcher.Run(args);
            Console.Error.WriteLine("FAILED: invalid BenchmarkDotNet arguments.");
            return 1;
        }

        if (options.PrintInformation || options.ListBenchmarkCaseMode != ListBenchmarkCaseMode.Disabled)
        {
            switcher.Run(args);
            return 0;
        }

        IConfig config = ManualConfig.Union(DefaultConfig.Instance, parsedConfig);
        BenchmarkCase[] expected = TypeFilter.Filter(config, [benchmarkType])
            .SelectMany(info => info.BenchmarksCases)
            .ToArray();
        string directory = options.ArtifactsDirectory?.FullName ??
            Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts");
        Directory.CreateDirectory(directory);
        string id = Guid.NewGuid().ToString("N");
        string incompletePath = Path.Combine(directory, $"INCOMPLETE-{id}.txt");
        string failedPath = Path.Combine(directory, $"FAILED-{id}.txt");
        File.WriteAllText(incompletePath, "INCOMPLETE: baseline benchmark results have not been validated.");

        try
        {
            Summary[] summaries = switcher.Run(args).ToArray();
            BenchmarkRunValidation.ValidateBaseline(summaries, expected);
            File.Delete(incompletePath);
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(failedPath, "FAILED: baseline benchmark results must not be used as a successful run.");
            string reason = exception is InvalidOperationException &&
                exception.Message.StartsWith("FAILED:", StringComparison.Ordinal)
                ? exception.Message
                : $"FAILED: baseline benchmark run ({exception.GetType().Name}).";
            Console.Error.WriteLine(
                $"{reason} Diagnostic artifacts: {directory}");
            return 1;
        }
    }
}
