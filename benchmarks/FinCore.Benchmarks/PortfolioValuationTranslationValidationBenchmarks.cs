using BenchmarkDotNet.Attributes;
using FinCore.Application.Portfolios;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

/// <summary>
/// Measures scalar and JSON valuation against the Phase 3.2D multi-trade validation workloads.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PortfolioValuationTranslationValidationBenchmarks
{
    private const string ConnectionStringEnvironmentVariable =
        "FINCORE_TRANSLATION_VALIDATION_SQLSERVER_CONNECTION_STRING";

    private ServiceProvider? _serviceProvider;
    private TranslationValidationData? _workload;

    /// <summary>
    /// Gets or sets the number of instruments in the selected deterministic validation workload.
    /// </summary>
    [Params(1, 5, 50, 500, 2_000, 2_001)]
    public int InstrumentCount { get; set; }

    /// <summary>
    /// Gets or sets whether to use the opt-in EF Core JSON collection parameter translation.
    /// </summary>
    [Params(false, true)]
    public bool UseJsonParameter { get; set; }

    /// <summary>
    /// Connects to the parent-owned benchmark database and verifies the expected result.
    /// </summary>
    /// <returns>A task completed after setup and correctness verification.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the owned-database connection or expected valuation cannot be verified.
    /// </exception>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} only through the translation validation runner.");
        await BenchmarkDatabaseFixture.ValidateWorkerDatabaseAsync(connectionString);
        _workload = TranslationValidationWorkload.Create(InstrumentCount);
        _serviceProvider = BenchmarkServiceProviderFactory.Create(
            connectionString,
            useJsonCollectionParameter: UseJsonParameter);
        PortfolioValuation valuation = await ValueAsync(_serviceProvider, _workload);
        TranslationValidationWorkload.ValidateValuation(_workload, valuation);
    }

    /// <summary>
    /// Measures one warm valuation for the selected mode and workload.
    /// </summary>
    /// <returns>A task completed after one valuation result has been produced.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when BenchmarkDotNet has not completed global setup.
    /// </exception>
    [Benchmark]
    public async Task ValuePortfolioAsync()
    {
        TranslationValidationData workload = _workload ??
            throw new InvalidOperationException("Benchmark global setup did not initialize the workload.");
        ServiceProvider serviceProvider = _serviceProvider ??
            throw new InvalidOperationException("Benchmark global setup did not initialize the services.");
        await ValueAsync(serviceProvider, workload);
    }

    /// <summary>
    /// Disposes the worker service provider without changing the parent-owned database lifecycle.
    /// </summary>
    /// <returns>A task that completes after service disposal.</returns>
    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
            _serviceProvider = null;
        }
    }

    private static async Task<PortfolioValuation> ValueAsync(
        ServiceProvider serviceProvider,
        TranslationValidationData workload)
    {
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
        IPortfolioValuationApplicationService service =
            scope.ServiceProvider.GetRequiredService<IPortfolioValuationApplicationService>();
        return await service.GetPortfolioValuationAsync(
            workload.PortfolioId,
            workload.AsOfUtc,
            CancellationToken.None);
    }
}
