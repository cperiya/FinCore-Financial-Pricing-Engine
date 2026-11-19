namespace FinCore.Benchmarks;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--harness-self-test")
        {
            BenchmarkValidationSelfTest.Run();
            return 0;
        }

        if (args.Length == 1 && args[0] == "--harness-sql-failure-test")
        {
            await BenchmarkValidationSelfTest.RunOwnedDatabaseFailureAsync();
            return 0;
        }

        if (args.Length == 1 && args[0] == "--harness-setup-failure-test")
        {
            await BenchmarkValidationSelfTest.RunSetupFailureAsync();
            return 0;
        }

        if (args.Length > 0 && args[0] == "--harness-default-smoke")
        {
            return BaselineBenchmarkRunner.Run(typeof(BenchmarkValidationSmokeBenchmarks), args[1..]);
        }

        if (args.Length > 0 && args[0] == "--e2e")
        {
            MeasurementOptions options = MeasurementOptions.Parse(args[1..]);
            await EndToEndMeasurementRunner.RunAsync(options);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--diagnostics")
        {
            MeasurementOptions options = MeasurementOptions.Parse(args[1..]);
            await EndToEndMeasurementRunner.RunDiagnosticsAsync(options);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--stage-diagnostics")
        {
            MeasurementOptions options = MeasurementOptions.Parse(args[1..]);
            await EndToEndMeasurementRunner.RunStageDiagnosticsAsync(options);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--translation-experiment")
        {
            MeasurementOptions options = MeasurementOptions.Parse(
                args[1..],
                defaultSamples: 100,
                defaultWarmup: 10);
            await CollectionParameterExperimentRunner.RunAsync(options);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--translation-validation")
        {
            MeasurementOptions options = MeasurementOptions.Parse(
                args[1..],
                defaultSamples: 100,
                defaultWarmup: 10);
            await CollectionParameterValidationRunner.RunAsync(options);
            return 0;
        }

        if (args.Length > 0 && args[0] == "--allocation-profile")
        {
            if (args.Length != 5 ||
                !int.TryParse(args[1], out int instrumentCount) ||
                !int.TryParse(args[2], out int sampleCount) ||
                !int.TryParse(args[3], out int warmupCount) ||
                !SyntheticPortfolioWorkload.InstrumentCounts.Contains(instrumentCount) ||
                sampleCount is < 1 or > 100 ||
                warmupCount is < 0 or > 20 ||
                !Guid.TryParseExact(args[4], "N", out Guid sessionId))
            {
                throw new ArgumentException(
                    "Usage: --allocation-profile <5|50|500|2000> <samples:1-100> " +
                    "<warmups:0-20> <session-guid-N>.");
            }

            await AllocationProfileRunner.RunAsync(
                instrumentCount,
                sampleCount,
                warmupCount,
                sessionId);
            return 0;
        }

        if (args.Length == 2 && args[0] == "--cleanup")
        {
            await BenchmarkDatabaseFixture.CleanupOwnedDatabaseAsync(args[1]);
            return 0;
        }

        if (args.Length == 1 && args[0] == "--protected-check")
        {
            ProtectedDatabaseSnapshot snapshot =
                await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
            Console.WriteLine(snapshot);
            return 0;
        }

        if (args.Length == 1 && args[0] == "--owned-check")
        {
            IReadOnlyList<string> databases =
                await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
            Console.WriteLine(
                databases.Count == 0
                    ? "No FinCoreBenchmarkRun_<GUID> databases remain."
                    : string.Join(Environment.NewLine, databases));
            return 0;
        }

        if (args.Length > 0 && args[0].StartsWith("--cleanup", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Use --cleanup FinCoreBenchmarkRun_<32-character-GUID> to clean one interrupted run.");
        }

        return BaselineBenchmarkRunner.Run(typeof(PortfolioValuationBenchmarks), args);
    }
}
