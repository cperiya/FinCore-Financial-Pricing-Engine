using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Running;
using FinCore.Application.Portfolios;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class CollectionParameterValidationRunner
{
    private const string WorkerConnectionStringEnvironmentVariable =
        "FINCORE_TRANSLATION_VALIDATION_SQLSERVER_CONNECTION_STRING";

    public static async Task RunAsync(MeasurementOptions options)
    {
        if (options.Samples > 200 || options.Warmup > 30)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Final validation is bounded to 200 measured calls and 30 warm-ups per mode and workload.");
        }

        string repositoryRoot = FindRepositoryRoot();
        string runDirectory = Path.Combine(
            repositoryRoot,
            "BenchmarkDotNet.Artifacts",
            "TranslationValidation",
            $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        string bdnDirectory = Path.Combine(runDirectory, "BenchmarkDotNet");
        Directory.CreateDirectory(bdnDirectory);
        string incompletePath = Path.Combine(runDirectory, "INCOMPLETE.txt");
        File.WriteAllText(incompletePath, "INCOMPLETE: translation validation has not completed successfully.");

        ProtectedDatabaseSnapshot protectedBefore =
            await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedBefore =
            await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        List<TranslationValidationCommandShape> commandShapes = [];
        List<TranslationValidationMeasurement> measurements = [];
        List<SqlPlanSnapshot> plans = [];
        SqlServerEnvironment? sqlServer = null;
        string? databaseName = null;
        int compatibilityLevel = 0;
        Exception? runFailure = null;

        try
        {
            await using BenchmarkDatabaseFixture database = new();
            await database.InitializeAsync();
            databaseName = database.DatabaseName;
            sqlServer = database.ServerEnvironment;
            compatibilityLevel = await database.ReadDatabaseCompatibilityLevelAsync();
            if (compatibilityLevel < 130)
            {
                throw new InvalidOperationException(
                    $"The run-owned benchmark database compatibility level is {compatibilityLevel}; " +
                    "OPENJSON requires level 130 or higher. No compatibility change was made.");
            }

            foreach (int instrumentCount in TranslationValidationWorkload.InstrumentCounts)
            {
                await TranslationValidationWorkload.SeedAsync(
                    database.BenchmarkConnectionString,
                    TranslationValidationWorkload.Create(instrumentCount));
            }

            Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>> commands =
                await VerifyTranslationAndCaptureCommandsAsync(
                    database.BenchmarkConnectionString,
                    commandShapes);
            foreach (int instrumentCount in TranslationValidationWorkload.InstrumentCounts)
            {
                foreach (bool useJson in new[] { false, true })
                {
                    SqlCommandDefinition[] planCommands = commands[(instrumentCount, useJson)]
                        .Where(command =>
                            command.SqlText.Contains("[Instruments]", StringComparison.OrdinalIgnoreCase) ||
                            command.SqlText.Contains("[MarketPrices]", StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    plans.AddRange(await SqlPlanCapture.CaptureExecutedPlansAsync(
                        database.BenchmarkConnectionString,
                        planCommands));
                }
            }

            measurements = await RunPairedMeasurementsAsync(
                database.BenchmarkConnectionString,
                options);
            RunBenchmarkDotNet(database.BenchmarkConnectionString, bdnDirectory);
        }
        catch (Exception exception)
        {
            runFailure = exception;
        }

        ProtectedDatabaseSnapshot protectedAfter =
            await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedAfter =
            await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        if (protectedBefore != protectedAfter)
        {
            throw new InvalidOperationException(
                "The protected database snapshot changed during translation validation.",
                runFailure);
        }

        if (!ownedBefore.SequenceEqual(ownedAfter, StringComparer.Ordinal) ||
            (databaseName is not null && ownedAfter.Contains(databaseName, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                "Translation validation did not clean up only its owned benchmark database.",
                runFailure);
        }

        if (runFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(runFailure).Throw();
        }

        if (databaseName is null || sqlServer is null)
        {
            throw new InvalidOperationException("The isolated validation database was not initialized.");
        }

        BenchmarkEnvironment environment = await BenchmarkEnvironment.CaptureAsync(sqlServer);
        string commandShapePath = WriteJson(
            runDirectory,
            "sql-command-shapes.json",
            commandShapes);
        string planPath = WriteJson(runDirectory, "actual-execution-plans.json", plans);
        string reportPath = WriteJson(
            runDirectory,
            "validation-report.json",
            new TranslationValidationReport(
                databaseName,
                DateTimeOffset.UtcNow,
                compatibilityLevel,
                environment,
                options,
                commandShapes,
                measurements,
                plans.Count,
                bdnDirectory,
                commandShapePath,
                planPath,
                protectedBefore,
                ownedBefore));

        Console.WriteLine($"Validation database: {databaseName} (compatibility {compatibilityLevel})");
        Console.WriteLine(
            "Workload  Mode    Samples  First-new-provider(ms)  p50(ms)  p95(ms)  p99(ms)  " +
            "Mean(ms)  SD(ms)  SQL readers  SQL avg(ms)  Calls/sec");
        foreach (TranslationValidationMeasurement measurement in measurements)
        {
            Console.WriteLine(
                $"{measurement.InstrumentCount,8}  {(measurement.UseJsonParameter ? "JSON" : "scalar"),-6}  " +
                $"{measurement.SampleCount,7}  {measurement.FirstNewProviderMilliseconds,22:F3}  " +
                $"{measurement.P50Milliseconds,7:F3}  {measurement.P95Milliseconds,7:F3}  " +
                $"{measurement.P99Milliseconds,7:F3}  {measurement.MeanMilliseconds,8:F3}  " +
                $"{measurement.StandardDeviationMilliseconds,6:F3}  " +
                $"{measurement.MeanReaderCommands,11:F2}  {measurement.MeanReaderDurationMilliseconds,11:F3}  " +
                $"{measurement.CallsPerSecond,9:F2}");
        }

        Console.WriteLine($"BenchmarkDotNet artifacts: {bdnDirectory}");
        Console.WriteLine($"SQL shape report: {commandShapePath}");
        Console.WriteLine($"Actual plan report: {planPath}");
        Console.WriteLine($"Validation report: {reportPath}");
        File.Delete(incompletePath);
    }

    private static async Task<Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>>>
        VerifyTranslationAndCaptureCommandsAsync(
            string connectionString,
            ICollection<TranslationValidationCommandShape> commandShapes)
    {
        Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>> result = [];
        foreach (int instrumentCount in TranslationValidationWorkload.InstrumentCounts)
        {
            TranslationValidationData workload = TranslationValidationWorkload.Create(instrumentCount);
            foreach (bool useJson in new[] { false, true })
            {
                SqlCommandCaptureInterceptor interceptor = new();
                await using ServiceProvider provider = BenchmarkServiceProviderFactory.Create(
                    connectionString,
                    interceptor,
                    useJsonCollectionParameter: useJson);
                PortfolioValuation valuation = await ValueAsync(provider, workload);
                TranslationValidationWorkload.ValidateValuation(workload, valuation);

                IReadOnlyList<SqlCommandDefinition> captured = interceptor.Commands;
                TranslationValidationCommandShape shape = ValidateCommandBatches(
                    workload,
                    useJson,
                    captured);
                commandShapes.Add(shape);
                result.Add((instrumentCount, useJson), captured);
            }
        }

        return result;
    }

    private static TranslationValidationCommandShape ValidateCommandBatches(
        TranslationValidationData workload,
        bool useJson,
        IReadOnlyList<SqlCommandDefinition> commands)
    {
        SqlCommandDefinition[] instrumentCommands = commands
            .Where(command => command.SqlText.Contains(
                "[Instruments]",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SqlCommandDefinition[] marketPriceCommands = commands
            .Where(command => command.SqlText.Contains(
                "[MarketPrices]",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        int expectedBatchCount = (workload.InstrumentCount + 1_999) / 2_000;
        if (instrumentCommands.Length != expectedBatchCount ||
            marketPriceCommands.Length != expectedBatchCount)
        {
            throw new InvalidOperationException(
                $"The {workload.InstrumentCount}-ID workload used unexpected repository batch counts.");
        }

        int[] instrumentBatchSizes = ValidateBatches(
            instrumentCommands,
            workload,
            useJson,
            expectCutoff: false);
        int[] priceBatchSizes = ValidateBatches(
            marketPriceCommands,
            workload,
            useJson,
            expectCutoff: true);

        bool allCommandsUseExpectedTranslation = instrumentCommands
            .Concat(marketPriceCommands)
            .All(command => command.SqlText.Contains(
                "OPENJSON",
                StringComparison.OrdinalIgnoreCase) == useJson);
        bool hasHistoricalCutoff = marketPriceCommands.All(command =>
            command.SqlText.Contains("[AsOfUtc]", StringComparison.OrdinalIgnoreCase) &&
            command.SqlText.Contains("<=", StringComparison.Ordinal) &&
            command.Parameters.Any(parameter =>
                parameter.Value is DateTimeOffset cutoff && cutoff == workload.AsOfUtc));
        bool hasDeterministicTieBreak = marketPriceCommands.All(command =>
            command.SqlText.Contains("[AsOfUtc] DESC", StringComparison.OrdinalIgnoreCase) &&
            command.SqlText.Contains("[Id] DESC", StringComparison.OrdinalIgnoreCase));
        if (!allCommandsUseExpectedTranslation || !hasHistoricalCutoff || !hasDeterministicTieBreak)
        {
            throw new InvalidOperationException(
                "Generated SQL changed collection translation, the UTC cutoff, or deterministic price ordering.");
        }

        return new TranslationValidationCommandShape(
            workload.InstrumentCount,
            useJson,
            instrumentBatchSizes,
            priceBatchSizes,
            instrumentCommands.Select(command => command.Parameters.Count).ToArray(),
            marketPriceCommands.Select(command => command.Parameters.Count).ToArray(),
            hasHistoricalCutoff,
            hasDeterministicTieBreak,
            instrumentCommands.Select(command => command.SqlText).ToArray(),
            marketPriceCommands.Select(command => command.SqlText).ToArray());
    }

    private static int[] ValidateBatches(
        IReadOnlyList<SqlCommandDefinition> commands,
        TranslationValidationData workload,
        bool useJson,
        bool expectCutoff)
    {
        List<Guid> actualIds = [];
        List<int> batchSizes = [];
        foreach (SqlCommandDefinition command in commands)
        {
            Guid[] ids;
            if (useJson)
            {
                string[] jsonParameters = command.Parameters
                    .Select(parameter => parameter.Value as string)
                    .Where(value => value is not null &&
                        value.StartsWith("[", StringComparison.Ordinal) &&
                        value.EndsWith("]", StringComparison.Ordinal))
                    .Cast<string>()
                    .ToArray();
                if (jsonParameters.Length != 1)
                {
                    throw new InvalidOperationException(
                        "A JSON ID batch did not use exactly one JSON collection parameter.");
                }

                using JsonDocument document = JsonDocument.Parse(jsonParameters[0]);
                ids = document.RootElement
                    .EnumerateArray()
                    .Select(element => element.GetGuid())
                    .ToArray();
            }
            else
            {
                ids = command.Parameters
                    .Where(parameter => parameter.Value is Guid)
                    .Select(parameter => (Guid)parameter.Value!)
                    .ToArray();
            }

            if (ids.Length is < 1 or > 2_000)
            {
                throw new InvalidOperationException(
                    "A repository ID batch was empty or exceeded the existing 2,000-ID boundary.");
            }

            batchSizes.Add(ids.Length);
            actualIds.AddRange(ids);
            if (expectCutoff &&
                !command.Parameters.Any(parameter =>
                    parameter.Value is DateTimeOffset cutoff && cutoff == workload.AsOfUtc))
            {
                throw new InvalidOperationException(
                    "A market-price query did not retain the valuation's exact UTC cutoff parameter.");
            }
        }

        Guid[] expectedIds = workload.Instruments
            .Select(instrument => instrument.InstrumentId)
            .ToArray();
        if (actualIds.Count != expectedIds.Length ||
            actualIds.Distinct().Count() != actualIds.Count ||
            !actualIds.ToHashSet().SetEquals(expectedIds))
        {
            throw new InvalidOperationException(
                "The executed repository batches did not cover each expected instrument ID exactly once.");
        }

        return batchSizes.ToArray();
    }

    private static async Task<List<TranslationValidationMeasurement>> RunPairedMeasurementsAsync(
        string connectionString,
        MeasurementOptions options)
    {
        List<TranslationValidationMeasurement> measurements = [];
        foreach (int instrumentCount in TranslationValidationWorkload.InstrumentCounts)
        {
            TranslationValidationData workload = TranslationValidationWorkload.Create(instrumentCount);
            SqlCommandDiagnosticsInterceptor scalarDiagnostics = new();
            SqlCommandDiagnosticsInterceptor jsonDiagnostics = new();
            await using ServiceProvider scalarProvider = BenchmarkServiceProviderFactory.Create(
                connectionString,
                scalarDiagnostics);
            await using ServiceProvider jsonProvider = BenchmarkServiceProviderFactory.Create(
                connectionString,
                jsonDiagnostics,
                useJsonCollectionParameter: true);

            (double scalarFirst, _) = await MeasureAsync(
                scalarProvider,
                scalarDiagnostics,
                workload);
            (double jsonFirst, _) = await MeasureAsync(
                jsonProvider,
                jsonDiagnostics,
                workload);

            List<double> scalarSamples = new(options.Samples);
            List<double> jsonSamples = new(options.Samples);
            List<double> scalarSqlDurations = new(options.Samples);
            List<double> jsonSqlDurations = new(options.Samples);
            List<long> scalarCommandCounts = new(options.Samples);
            List<long> jsonCommandCounts = new(options.Samples);

            for (int warmup = 0; warmup < options.Warmup; warmup++)
            {
                bool jsonFirstInPair = warmup % 2 != 0;
                await MeasureAsync(
                    jsonFirstInPair ? jsonProvider : scalarProvider,
                    jsonFirstInPair ? jsonDiagnostics : scalarDiagnostics,
                    workload);
                await MeasureAsync(
                    jsonFirstInPair ? scalarProvider : jsonProvider,
                    jsonFirstInPair ? scalarDiagnostics : jsonDiagnostics,
                    workload);
            }

            for (int sample = 0; sample < options.Samples; sample++)
            {
                bool jsonFirstInPair = sample % 2 != 0;
                (double firstDuration, SqlCommandDiagnosticsSnapshot firstSql) =
                    await MeasureAsync(
                        jsonFirstInPair ? jsonProvider : scalarProvider,
                        jsonFirstInPair ? jsonDiagnostics : scalarDiagnostics,
                        workload);
                (double secondDuration, SqlCommandDiagnosticsSnapshot secondSql) =
                    await MeasureAsync(
                        jsonFirstInPair ? scalarProvider : jsonProvider,
                        jsonFirstInPair ? scalarDiagnostics : jsonDiagnostics,
                        workload);

                if (jsonFirstInPair)
                {
                    jsonSamples.Add(firstDuration);
                    jsonSqlDurations.Add(firstSql.ReaderExecuteDuration.TotalMilliseconds);
                    jsonCommandCounts.Add(firstSql.ReaderCommandCount);
                    scalarSamples.Add(secondDuration);
                    scalarSqlDurations.Add(secondSql.ReaderExecuteDuration.TotalMilliseconds);
                    scalarCommandCounts.Add(secondSql.ReaderCommandCount);
                }
                else
                {
                    scalarSamples.Add(firstDuration);
                    scalarSqlDurations.Add(firstSql.ReaderExecuteDuration.TotalMilliseconds);
                    scalarCommandCounts.Add(firstSql.ReaderCommandCount);
                    jsonSamples.Add(secondDuration);
                    jsonSqlDurations.Add(secondSql.ReaderExecuteDuration.TotalMilliseconds);
                    jsonCommandCounts.Add(secondSql.ReaderCommandCount);
                }
            }

            measurements.Add(CreateMeasurement(
                instrumentCount,
                useJson: false,
                scalarFirst,
                scalarSamples,
                scalarSqlDurations,
                scalarCommandCounts));
            measurements.Add(CreateMeasurement(
                instrumentCount,
                useJson: true,
                jsonFirst,
                jsonSamples,
                jsonSqlDurations,
                jsonCommandCounts));
        }

        return measurements;
    }

    private static async Task<(double DurationMilliseconds, SqlCommandDiagnosticsSnapshot Sql)> MeasureAsync(
        ServiceProvider provider,
        SqlCommandDiagnosticsInterceptor diagnostics,
        TranslationValidationData workload)
    {
        diagnostics.Reset();
        Stopwatch stopwatch = Stopwatch.StartNew();
        PortfolioValuation valuation = await ValueAsync(provider, workload);
        stopwatch.Stop();
        TranslationValidationWorkload.ValidateValuation(workload, valuation);
        return (stopwatch.Elapsed.TotalMilliseconds, diagnostics.Snapshot());
    }

    private static async Task<PortfolioValuation> ValueAsync(
        ServiceProvider provider,
        TranslationValidationData workload)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IPortfolioValuationApplicationService service =
            scope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>();
        return await service.GetPortfolioValuationAsync(
            workload.PortfolioId,
            workload.AsOfUtc,
            CancellationToken.None);
    }

    private static TranslationValidationMeasurement CreateMeasurement(
        int instrumentCount,
        bool useJson,
        double firstNewProviderMilliseconds,
        IReadOnlyList<double> samples,
        IReadOnlyList<double> sqlDurations,
        IReadOnlyList<long> readerCommandCounts)
    {
        double mean = samples.Average();
        double variance = samples.Sum(sample => Math.Pow(sample - mean, 2)) / samples.Count;
        double totalMeasuredMilliseconds = samples.Sum();
        return new TranslationValidationMeasurement(
            instrumentCount,
            useJson,
            samples.Count,
            firstNewProviderMilliseconds,
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            mean,
            Math.Sqrt(variance),
            readerCommandCounts.Average(),
            sqlDurations.Average(),
            samples.Count / (totalMeasuredMilliseconds / 1_000d));
    }

    private static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        double[] sorted = values.Order().ToArray();
        double position = percentile * (sorted.Length - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        double fraction = position - lower;
        return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
    }

    private static void RunBenchmarkDotNet(string connectionString, string artifactDirectory)
    {
        string? previousConnectionString =
            Environment.GetEnvironmentVariable(WorkerConnectionStringEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                WorkerConnectionStringEnvironmentVariable,
                connectionString);
            BenchmarkRunValidation.Run(
                () => BenchmarkSwitcher
                    .FromTypes([typeof(PortfolioValuationTranslationValidationBenchmarks)])
                    .Run(
                    [
                        "--filter",
                        "*PortfolioValuationTranslationValidationBenchmarks*",
                        "--artifacts",
                        artifactDirectory,
                        "--allStats",
                        "--join",
                        "--launchCount",
                        "3"
                    ]).ToArray(),
                typeof(PortfolioValuationTranslationValidationBenchmarks),
                TranslationValidationWorkload.InstrumentCounts,
                launchCount: 3,
                artifactDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                WorkerConnectionStringEnvironmentVariable,
                previousConnectionString);
        }
    }

    private static string WriteJson<T>(string directory, string fileName, T value)
    {
        string path = Path.Combine(directory, fileName);
        File.WriteAllBytes(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                value,
                new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new InvalidOperationException("Could not locate the repository root for benchmark artifacts.");
    }
}

internal sealed record TranslationValidationCommandShape(
    int InstrumentCount,
    bool UseJsonParameter,
    IReadOnlyList<int> InstrumentBatchSizes,
    IReadOnlyList<int> MarketPriceBatchSizes,
    IReadOnlyList<int> InstrumentParameterCounts,
    IReadOnlyList<int> MarketPriceParameterCounts,
    bool HasHistoricalCutoff,
    bool HasDeterministicTieBreak,
    IReadOnlyList<string> InstrumentSqlText,
    IReadOnlyList<string> MarketPriceSqlText);

internal sealed record TranslationValidationMeasurement(
    int InstrumentCount,
    bool UseJsonParameter,
    int SampleCount,
    double FirstNewProviderMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double MeanMilliseconds,
    double StandardDeviationMilliseconds,
    double MeanReaderCommands,
    double MeanReaderDurationMilliseconds,
    double CallsPerSecond);

internal sealed record TranslationValidationReport(
    string DatabaseName,
    DateTimeOffset CompletedAtUtc,
    int CompatibilityLevel,
    BenchmarkEnvironment Environment,
    MeasurementOptions MeasurementOptions,
    IReadOnlyList<TranslationValidationCommandShape> CommandShapes,
    IReadOnlyList<TranslationValidationMeasurement> Measurements,
    int ActualPlanCount,
    string BenchmarkDotNetArtifactDirectory,
    string CommandShapeReportPath,
    string ActualPlanReportPath,
    ProtectedDatabaseSnapshot ProtectedDatabasesBefore,
    IReadOnlyList<string> OwnedBenchmarkDatabasesBefore);
