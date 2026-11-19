using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

/// <summary>
/// Compares the default scalar ID parameters with the opt-in EF Core JSON collection parameter translation.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class PortfolioValuationTranslationBenchmarks
{
    private const string ConnectionStringEnvironmentVariable =
        "FINCORE_TRANSLATION_EXPERIMENT_SQLSERVER_CONNECTION_STRING";

    private ServiceProvider? _serviceProvider;
    private SyntheticWorkload? _workload;

    /// <summary>
    /// Gets or sets the synthetic workload size selected by BenchmarkDotNet.
    /// </summary>
    [Params(5, 50, 500, 2_000)]
    public int InstrumentCount { get; set; }

    /// <summary>
    /// Gets or sets whether this case uses the opt-in JSON collection parameter translation.
    /// </summary>
    [Params(false, true)]
    public bool UseJsonParameter { get; set; }

    /// <summary>
    /// Connects to the parent-owned run database and verifies the expected valuation before measurement.
    /// </summary>
    /// <returns>A task that completes after setup and correctness verification.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the parent runner connection, ownership marker, or expected valuation cannot be verified.
    /// </exception>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        string connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) ??
            throw new InvalidOperationException(
                $"Set {ConnectionStringEnvironmentVariable} only through the translation experiment runner.");
        await BenchmarkDatabaseFixture.ValidateWorkerDatabaseAsync(connectionString);
        _workload = SyntheticPortfolioWorkload.Create(InstrumentCount);
        _serviceProvider = BenchmarkServiceProviderFactory.Create(
            connectionString,
            useJsonCollectionParameter: UseJsonParameter);

        FinCore.Application.Portfolios.PortfolioValuation valuation =
            await BenchmarkServiceProviderFactory.ValueAsync(_serviceProvider, _workload);
        SyntheticPortfolioWorkload.ValidateValuation(_workload, valuation);
    }

    /// <summary>
    /// Measures one warm valuation while retaining the existing sequential transaction and query flow.
    /// </summary>
    /// <returns>A task completed after one valuation result has been produced.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when BenchmarkDotNet has not completed global setup.
    /// </exception>
    [Benchmark]
    public async Task ValuePortfolioAsync()
    {
        SyntheticWorkload workload = _workload ??
            throw new InvalidOperationException("Benchmark global setup did not initialize the workload.");
        ServiceProvider serviceProvider = _serviceProvider ??
            throw new InvalidOperationException("Benchmark global setup did not initialize the services.");

        await BenchmarkServiceProviderFactory.ValueAsync(serviceProvider, workload);
    }

    /// <summary>
    /// Disposes the worker's service provider without changing the parent-owned database lifecycle.
    /// </summary>
    /// <returns>A task that completes after worker service disposal.</returns>
    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
            _serviceProvider = null;
        }
    }
}
