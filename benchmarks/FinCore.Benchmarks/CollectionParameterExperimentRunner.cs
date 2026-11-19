using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Running;
using FinCore.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class CollectionParameterExperimentRunner
{
    private const string WorkerConnectionStringEnvironmentVariable =
        "FINCORE_TRANSLATION_EXPERIMENT_SQLSERVER_CONNECTION_STRING";

    /// <summary>
    /// Runs matched scalar and JSON collection-parameter measurements on one isolated benchmark database.
    /// </summary>
    /// <param name="options">The bounded end-to-end sample and warm-up counts.</param>
    /// <returns>A task that completes after measurements, safe database cleanup, and report creation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the configured samples or warmups exceed the experiment's bounds.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when database ownership, SQL translation, workload correctness, or cleanup cannot be verified.
    /// </exception>
    public static async Task RunAsync(MeasurementOptions options)
    {
        if (options.Samples > 500 || options.Warmup > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The paired SQL experiment is bounded to at most 500 samples and 100 warmups per variant and workload.");
        }

        string repositoryRoot = FindRepositoryRoot();
        string runDirectoryName =
            $"run-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}";
        string artifactDirectory = Path.Combine(
            repositoryRoot,
            "BenchmarkDotNet.Artifacts",
            "TranslationExperiment",
            runDirectoryName);
        string bdnArtifactDirectory = Path.Combine(artifactDirectory, "BenchmarkDotNet");
        Directory.CreateDirectory(bdnArtifactDirectory);
        string incompletePath = Path.Combine(artifactDirectory, "INCOMPLETE.txt");
        File.WriteAllText(incompletePath, "INCOMPLETE: translation experiment has not completed successfully.");

        ProtectedDatabaseSnapshot protectedDatabasesBefore =
            await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedDatabasesBefore =
            await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        string databaseName;
        int compatibilityLevel;
        SqlServerEnvironment sqlServer;
        List<TranslationCommandShape> commandShapes = [];
        List<TranslationEndToEndMeasurement> endToEnd = [];
        List<TranslationSqlDiagnosticMeasurement> sqlDiagnostics = [];
        List<TranslationStageDiagnosticMeasurement> stageDiagnostics = [];
        List<SqlPlanSnapshot> plans = [];
        string jsonShapePath;
        string planPath;

        await using (BenchmarkDatabaseFixture database = new())
        {
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

            foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
            {
                await SyntheticPortfolioWorkload.SeedAsync(
                    database.BenchmarkConnectionString,
                    SyntheticPortfolioWorkload.Create(instrumentCount));
            }

            Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>> planCommands =
                await VerifyTranslationAndCaptureCommandsAsync(
                    database.BenchmarkConnectionString,
                    commandShapes);
            foreach (int instrumentCount in new[] { 500, 2_000 })
            {
                foreach (bool useJson in new[] { false, true })
                {
                    IReadOnlyList<SqlCommandDefinition> relevantCommands =
                        planCommands[(instrumentCount, useJson)]
                            .Where(command =>
                                command.SqlText.Contains("[Instruments]", StringComparison.OrdinalIgnoreCase) ||
                                command.SqlText.Contains("[MarketPrices]", StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                    plans.AddRange(await SqlPlanCapture.CaptureExecutedPlansAsync(
                        database.BenchmarkConnectionString,
                        relevantCommands));
                }
            }

            jsonShapePath = WriteJson(
                artifactDirectory,
                $"sql-translation-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json",
                commandShapes);
            planPath = WriteJson(
                artifactDirectory,
                $"actual-plans-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json",
                plans);

            string? previousWorkerConnectionString =
                Environment.GetEnvironmentVariable(WorkerConnectionStringEnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(
                    WorkerConnectionStringEnvironmentVariable,
                    database.BenchmarkConnectionString);
                BenchmarkRunValidation.Run(
                    () => BenchmarkSwitcher
                        .FromTypes([typeof(PortfolioValuationTranslationBenchmarks)])
                        .Run(
                        [
                            "--filter",
                            "*PortfolioValuationTranslationBenchmarks*",
                            "--artifacts",
                            bdnArtifactDirectory,
                            "--allStats",
                            "--join",
                            "--launchCount",
                            "3"
                        ]).ToArray(),
                    typeof(PortfolioValuationTranslationBenchmarks),
                    SyntheticPortfolioWorkload.InstrumentCounts,
                    launchCount: 3,
                    bdnArtifactDirectory);
            }
            finally
            {
                Environment.SetEnvironmentVariable(
                    WorkerConnectionStringEnvironmentVariable,
                    previousWorkerConnectionString);
            }

            endToEnd = await RunPairedEndToEndAsync(
                database.BenchmarkConnectionString,
                options);
            (sqlDiagnostics, stageDiagnostics) = await RunSqlDiagnosticsAsync(
                database.BenchmarkConnectionString,
                options,
                commandShapes);
        }

        ProtectedDatabaseSnapshot protectedDatabasesAfter =
            await BenchmarkDatabaseFixture.ReadProtectedDatabaseSnapshotAsync();
        IReadOnlyList<string> ownedDatabasesAfter =
            await BenchmarkDatabaseFixture.ReadOwnedDatabaseNamesAsync();
        if (protectedDatabasesBefore != protectedDatabasesAfter)
        {
            throw new InvalidOperationException(
                "The protected database snapshot changed during the experiment.");
        }

        if (!ownedDatabasesBefore.SequenceEqual(ownedDatabasesAfter, StringComparer.Ordinal) ||
            ownedDatabasesAfter.Contains(databaseName, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Benchmark database cleanup did not preserve the pre-existing owned-database set.");
        }

        BenchmarkEnvironment environment = await BenchmarkEnvironment.CaptureAsync(sqlServer);
        string reportPath = WriteJson(
            artifactDirectory,
            $"experiment-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}.json",
            new CollectionParameterExperimentReport(
                databaseName,
                DateTimeOffset.UtcNow,
                compatibilityLevel,
                environment,
                options,
                commandShapes,
                endToEnd,
                sqlDiagnostics,
                stageDiagnostics,
                bdnArtifactDirectory,
                jsonShapePath,
                planPath,
                protectedDatabasesBefore,
                ownedDatabasesBefore));

        PrintReport(
            databaseName,
            compatibilityLevel,
            environment,
            commandShapes,
            endToEnd,
            sqlDiagnostics,
            stageDiagnostics,
            bdnArtifactDirectory,
            jsonShapePath,
            planPath,
            reportPath);
        File.Delete(incompletePath);
    }

    private static async Task<Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>>>
        VerifyTranslationAndCaptureCommandsAsync(
            string connectionString,
            ICollection<TranslationCommandShape> commandShapes)
    {
        Dictionary<(int InstrumentCount, bool UseJson), IReadOnlyList<SqlCommandDefinition>> commandsByVariant = [];
        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            foreach (bool useJson in new[] { false, true })
            {
                SqlCommandDiagnosticsInterceptor interceptor = new();
                ValuationDiagnosticsCollector collector = new(interceptor);
                await using ServiceProvider serviceProvider = BenchmarkServiceProviderFactory.Create(
                    connectionString,
                    interceptor,
                    collector,
                    useJson);
                FinCore.Application.Portfolios.PortfolioValuation valuation =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);

                IReadOnlyList<SqlCommandDefinition> capturedCommands = interceptor.Commands;
                commandShapes.Add(ValidateAndDescribeTranslation(
                    instrumentCount,
                    useJson,
                    workload,
                    capturedCommands));
                commandsByVariant.Add((instrumentCount, useJson), capturedCommands);
            }
        }

        return commandsByVariant;
    }

    private static TranslationCommandShape ValidateAndDescribeTranslation(
        int instrumentCount,
        bool useJson,
        SyntheticWorkload workload,
        IReadOnlyList<SqlCommandDefinition> commands)
    {
        SqlCommandDefinition instrumentCommand = FindSingleCommand(commands, "[Instruments]");
        SqlCommandDefinition marketPriceCommand = FindSingleCommand(commands, "[MarketPrices]");
        ValidateCollectionPredicate(instrumentCommand, workload, useJson, instrumentCount);
        ValidateCollectionPredicate(marketPriceCommand, workload, useJson, instrumentCount);

        bool hasHistoricalCutoff =
            marketPriceCommand.SqlText.Contains("[AsOfUtc]", StringComparison.OrdinalIgnoreCase) &&
            marketPriceCommand.SqlText.Contains("<=", StringComparison.Ordinal);
        bool hasDeterministicTieBreak =
            marketPriceCommand.SqlText.Contains("[AsOfUtc] DESC", StringComparison.OrdinalIgnoreCase) &&
            marketPriceCommand.SqlText.Contains("[Id] DESC", StringComparison.OrdinalIgnoreCase);
        if (!hasHistoricalCutoff || !hasDeterministicTieBreak)
        {
            throw new InvalidOperationException(
                "The market-price SQL lost its historical cutoff or deterministic timestamp/ID ordering.");
        }

        return new TranslationCommandShape(
            instrumentCount,
            useJson,
            instrumentCommand.Parameters.Count,
            marketPriceCommand.Parameters.Count,
            instrumentCommand.SqlText,
            marketPriceCommand.SqlText,
            hasHistoricalCutoff,
            hasDeterministicTieBreak);
    }

    private static void ValidateCollectionPredicate(
        SqlCommandDefinition command,
        SyntheticWorkload workload,
        bool useJson,
        int instrumentCount)
    {
        bool usesOpenJson = command.SqlText.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase);
        if (usesOpenJson != useJson)
        {
            throw new InvalidOperationException(
                $"The {(useJson ? "JSON" : "scalar")} mode produced an unexpected collection SQL translation.");
        }

        Guid[] expectedIds = workload.Instruments
            .Select(instrument => instrument.InstrumentId)
            .ToArray();
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
                    "The JSON collection query did not use exactly one JSON ID parameter.");
            }

            using JsonDocument document = JsonDocument.Parse(jsonParameters[0]);
            Guid[] actualIds = document.RootElement
                .EnumerateArray()
                .Select(element => element.GetGuid())
                .ToArray();
            if (!actualIds.ToHashSet().SetEquals(expectedIds))
            {
                throw new InvalidOperationException(
                    "The JSON collection parameter did not contain the expected instrument IDs.");
            }
        }
        else
        {
            Guid[] scalarIds = command.Parameters
                .Where(parameter => parameter.Value is Guid)
                .Select(parameter => (Guid)parameter.Value!)
                .ToArray();
            if (scalarIds.Length != instrumentCount ||
                !scalarIds.ToHashSet().SetEquals(expectedIds))
            {
                throw new InvalidOperationException(
                    "The scalar collection query did not contain the expected individual ID parameters.");
            }
        }
    }

    private static SqlCommandDefinition FindSingleCommand(
        IReadOnlyList<SqlCommandDefinition> commands,
        string tableName)
    {
        SqlCommandDefinition[] matches = commands
            .Where(command => command.SqlText.Contains(tableName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(
                $"Expected one captured SQL command for {tableName}; found {matches.Length}.");
    }

    private static async Task<List<TranslationEndToEndMeasurement>> RunPairedEndToEndAsync(
        string connectionString,
        MeasurementOptions options)
    {
        await using ServiceProvider scalarProvider =
            BenchmarkServiceProviderFactory.Create(connectionString);
        await using ServiceProvider jsonProvider =
            BenchmarkServiceProviderFactory.Create(
                connectionString,
                useJsonCollectionParameter: true);

        List<TranslationEndToEndMeasurement> measurements = [];
        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            for (int index = 0; index < options.Warmup; index++)
            {
                bool jsonFirst = index % 2 != 0;
                await MeasureAndValidateAsync(
                    jsonFirst ? jsonProvider : scalarProvider,
                    workload);
                await MeasureAndValidateAsync(
                    jsonFirst ? scalarProvider : jsonProvider,
                    workload);
            }

            List<double> scalarSamples = new(options.Samples);
            List<double> jsonSamples = new(options.Samples);
            for (int index = 0; index < options.Samples; index++)
            {
                bool jsonFirst = index % 2 != 0;
                (double firstElapsed, double secondElapsed) = await MeasurePairAsync(
                    jsonFirst ? jsonProvider : scalarProvider,
                    jsonFirst ? scalarProvider : jsonProvider,
                    workload);
                (jsonFirst ? jsonSamples : scalarSamples).Add(firstElapsed);
                (jsonFirst ? scalarSamples : jsonSamples).Add(secondElapsed);
            }

            measurements.Add(CreateEndToEndMeasurement(instrumentCount, false, scalarSamples));
            measurements.Add(CreateEndToEndMeasurement(instrumentCount, true, jsonSamples));
        }

        return measurements;
    }

    private static async Task<(double ElapsedMilliseconds, FinCore.Application.Portfolios.PortfolioValuation Valuation)>
        MeasureAndValidateAsync(
            ServiceProvider serviceProvider,
            SyntheticWorkload workload)
    {
        long startedAt = Stopwatch.GetTimestamp();
        FinCore.Application.Portfolios.PortfolioValuation valuation =
            await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
        double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
        SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
        return (elapsedMilliseconds, valuation);
    }

    private static async Task<(double FirstMilliseconds, double SecondMilliseconds)> MeasurePairAsync(
        ServiceProvider firstProvider,
        ServiceProvider secondProvider,
        SyntheticWorkload workload)
    {
        (double firstMilliseconds, _) = await MeasureAndValidateAsync(firstProvider, workload);
        (double secondMilliseconds, _) = await MeasureAndValidateAsync(secondProvider, workload);
        return (firstMilliseconds, secondMilliseconds);
    }

    private static TranslationEndToEndMeasurement CreateEndToEndMeasurement(
        int instrumentCount,
        bool useJson,
        IReadOnlyCollection<double> samples)
    {
        double summedMilliseconds = samples.Sum();
        return new TranslationEndToEndMeasurement(
            instrumentCount,
            useJson,
            samples.Count,
            Percentile(samples, 0.50),
            Percentile(samples, 0.95),
            Percentile(samples, 0.99),
            summedMilliseconds,
            samples.Count / (summedMilliseconds / 1_000d));
    }

    private static async Task<(
        List<TranslationSqlDiagnosticMeasurement> Sql,
        List<TranslationStageDiagnosticMeasurement> Stages)> RunSqlDiagnosticsAsync(
            string connectionString,
            MeasurementOptions options,
            ICollection<TranslationCommandShape> commandShapes)
    {
        List<TranslationSqlDiagnosticMeasurement> sqlMeasurements = [];
        List<TranslationStageDiagnosticMeasurement> stageMeasurements = [];
        foreach (int instrumentCount in SyntheticPortfolioWorkload.InstrumentCounts)
        {
            SyntheticWorkload workload = SyntheticPortfolioWorkload.Create(instrumentCount);
            foreach (bool useJson in new[] { false, true })
            {
                SqlCommandDiagnosticsInterceptor interceptor = new();
                ValuationDiagnosticsCollector collector = new(interceptor);
                await using ServiceProvider serviceProvider = BenchmarkServiceProviderFactory.Create(
                    connectionString,
                    interceptor,
                    collector,
                    useJson);

                FinCore.Application.Portfolios.PortfolioValuation verification =
                    await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                SyntheticPortfolioWorkload.ValidateValuation(workload, verification);
                TranslationCommandShape shape = ValidateAndDescribeTranslation(
                    instrumentCount,
                    useJson,
                    workload,
                    interceptor.Commands);
                commandShapes.Add(shape);

                for (int index = 0; index < options.Warmup; index++)
                {
                    FinCore.Application.Portfolios.PortfolioValuation warmup =
                        await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                    SyntheticPortfolioWorkload.ValidateValuation(workload, warmup);
                }

                collector.Reset();
                for (int index = 0; index < options.Samples; index++)
                {
                    FinCore.Application.Portfolios.PortfolioValuation valuation =
                        await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
                    SyntheticPortfolioWorkload.ValidateValuation(workload, valuation);
                }

                SqlCommandDiagnosticsSnapshot total = interceptor.Snapshot();
                SqlCommandDiagnosticsSnapshot instrument = interceptor.Snapshot(
                    "instrument.retrieval-and-materialization");
                SqlCommandDiagnosticsSnapshot marketPrices = interceptor.Snapshot(
                    "market-price.retrieval-and-materialization");
                sqlMeasurements.Add(new TranslationSqlDiagnosticMeasurement(
                    instrumentCount,
                    useJson,
                    options.Samples,
                    total.ReaderCommandCount / (double)options.Samples,
                    total.ReaderExecuteDuration.TotalMilliseconds / options.Samples,
                    instrument.ReaderCommandCount / (double)options.Samples,
                    instrument.ReaderExecuteDuration.TotalMilliseconds / options.Samples,
                    marketPrices.ReaderCommandCount / (double)options.Samples,
                    marketPrices.ReaderExecuteDuration.TotalMilliseconds / options.Samples));

                foreach (IGrouping<string, ValuationDiagnosticStage> group in collector.Stages
                             .GroupBy(stage => stage.StageName, StringComparer.Ordinal))
                {
                    stageMeasurements.Add(new TranslationStageDiagnosticMeasurement(
                        instrumentCount,
                        useJson,
                        group.Key,
                        options.Samples,
                        group.Average(stage => stage.ElapsedMilliseconds),
                        group.Average(stage => stage.ApproximateManagedAllocationsBytes),
                        group.Average(stage => stage.InputCount),
                        group.Average(stage => stage.OutputCount)));
                }

            }
        }

        return (sqlMeasurements, stageMeasurements);
    }

    private static double Percentile(IReadOnlyCollection<double> values, double percentile)
    {
        double[] ordered = values.Order().ToArray();
        int nearestRank = Math.Max(1, (int)Math.Ceiling(percentile * ordered.Length));
        return ordered[nearestRank - 1];
    }

    private static string WriteJson<T>(string directory, string fileName, T value)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, fileName);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static void PrintReport(
        string databaseName,
        int compatibilityLevel,
        BenchmarkEnvironment environment,
        IReadOnlyList<TranslationCommandShape> commandShapes,
        IReadOnlyList<TranslationEndToEndMeasurement> endToEnd,
        IReadOnlyList<TranslationSqlDiagnosticMeasurement> sqlDiagnostics,
        IReadOnlyList<TranslationStageDiagnosticMeasurement> stageDiagnostics,
        string bdnArtifactDirectory,
        string jsonShapePath,
        string planPath,
        string reportPath)
    {
        Console.WriteLine($"Database: {databaseName} (compatibility level {compatibilityLevel})");
        Console.WriteLine($"Environment: {environment.ToSummary()}");
        Console.WriteLine("Translation: scalar collections remain the default; JSON mode is query-local and opt-in.");
        Console.WriteLine("Generated SQL shapes (parameters only; values are not printed):");
        foreach (TranslationCommandShape shape in commandShapes
                     .DistinctBy(shape => (shape.InstrumentCount, shape.UseJsonParameter)))
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "  {0,4} instruments; {1,-6}; instrument params={2}; market-price params={3}; cutoff={4}; tie-break={5}",
                shape.InstrumentCount,
                shape.UseJsonParameter ? "JSON" : "scalar",
                shape.InstrumentParameterCount,
                shape.MarketPriceParameterCount,
                shape.HasHistoricalCutoff,
                shape.HasDeterministicTieBreak));
        }

        Console.WriteLine("Paired end-to-end samples (fresh service scopes; validation excluded from the timed interval):");
        Console.WriteLine("Count  Mode    Samples  p50(ms)  p95(ms)  p99(ms)  calls/sec");
        foreach (TranslationEndToEndMeasurement measurement in endToEnd)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,5}  {1,-6}  {2,7}  {3,7:F3}  {4,7:F3}  {5,7:F3}  {6,9:F2}",
                measurement.InstrumentCount,
                measurement.UseJsonParameter ? "JSON" : "scalar",
                measurement.SampleCount,
                measurement.P50Milliseconds,
                measurement.P95Milliseconds,
                measurement.P99Milliseconds,
                measurement.SequentialCallsPerSecond));
        }

        Console.WriteLine("SQL reader diagnostics (separate instrumented runs; not the BDN allocation measurement):");
        Console.WriteLine("Count  Mode    Commands/call  SQL ms/call  Instrument ms  Market-price ms");
        foreach (TranslationSqlDiagnosticMeasurement measurement in sqlDiagnostics)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,5}  {1,-6}  {2,13:F2}  {3,11:F3}  {4,14:F3}  {5,16:F3}",
                measurement.InstrumentCount,
                measurement.UseJsonParameter ? "JSON" : "scalar",
                measurement.ReaderCommandsPerCall,
                measurement.ReaderExecuteMillisecondsPerCall,
                measurement.InstrumentReaderMillisecondsPerCall,
                measurement.MarketPriceReaderMillisecondsPerCall));
        }

        Console.WriteLine(
            $"Approximate stage allocation samples: {stageDiagnostics.Count}; " +
            "collector stage deltas are not exact allocation attribution.");
        Console.WriteLine($"BDN artifacts: {bdnArtifactDirectory}");
        Console.WriteLine($"Generated SQL: {jsonShapePath}");
        Console.WriteLine($"Actual query plans: {planPath}");
        Console.WriteLine($"Experiment report: {reportPath}");
        Console.WriteLine("The owned database was dropped and protected database counts were verified unchanged.");
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
            "Could not locate FinCore.slnx; experiment results were not written.");
    }
}

internal sealed record TranslationCommandShape(
    int InstrumentCount,
    bool UseJsonParameter,
    int InstrumentParameterCount,
    int MarketPriceParameterCount,
    string InstrumentSql,
    string MarketPriceSql,
    bool HasHistoricalCutoff,
    bool HasDeterministicTieBreak);

internal sealed record TranslationEndToEndMeasurement(
    int InstrumentCount,
    bool UseJsonParameter,
    int SampleCount,
    double P50Milliseconds,
    double P95Milliseconds,
    double P99Milliseconds,
    double SummedCallMilliseconds,
    double SequentialCallsPerSecond);

internal sealed record TranslationSqlDiagnosticMeasurement(
    int InstrumentCount,
    bool UseJsonParameter,
    int SampleCount,
    double ReaderCommandsPerCall,
    double ReaderExecuteMillisecondsPerCall,
    double InstrumentReaderCommandsPerCall,
    double InstrumentReaderMillisecondsPerCall,
    double MarketPriceReaderCommandsPerCall,
    double MarketPriceReaderMillisecondsPerCall);

internal sealed record TranslationStageDiagnosticMeasurement(
    int InstrumentCount,
    bool UseJsonParameter,
    string StageName,
    int SampleCount,
    double AverageElapsedMilliseconds,
    double AverageApproximateAllocationsBytes,
    double AverageInputCount,
    double AverageOutputCount);

internal sealed record CollectionParameterExperimentReport(
    string DatabaseName,
    DateTimeOffset CapturedAtUtc,
    int DatabaseCompatibilityLevel,
    BenchmarkEnvironment Environment,
    MeasurementOptions MeasurementOptions,
    IReadOnlyList<TranslationCommandShape> CommandShapes,
    IReadOnlyList<TranslationEndToEndMeasurement> EndToEnd,
    IReadOnlyList<TranslationSqlDiagnosticMeasurement> SqlDiagnostics,
    IReadOnlyList<TranslationStageDiagnosticMeasurement> StageDiagnostics,
    string BenchmarkDotNetArtifactDirectory,
    string GeneratedSqlArtifactPath,
    string ActualPlanArtifactPath,
    ProtectedDatabaseSnapshot ProtectedDatabaseSnapshotBefore,
    IReadOnlyList<string> PreExistingOwnedDatabaseNames);
