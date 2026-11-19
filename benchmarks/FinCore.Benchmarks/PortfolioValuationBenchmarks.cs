using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

/// <summary>
/// Benchmarks the existing SQL-backed portfolio valuation application service for fixed synthetic workloads.
/// </summary>
[MemoryDiagnoser]
public class PortfolioValuationBenchmarks
{
    private BenchmarkDatabaseFixture? _database;
    private ServiceProvider? _serviceProvider;
    private SyntheticWorkload? _workload;
    internal Func<BenchmarkDatabaseFixture, Task>? AfterDatabaseInitializedForTest { get; set; }

    /// <summary>
    /// Gets or sets the synthetic instrument count selected by BenchmarkDotNet.
    /// </summary>
    [Params(5, 50, 500, 2_000)]
    public int InstrumentCount { get; set; }

    /// <summary>
    /// Creates and migrates an isolated benchmark database, seeds deterministic data, and validates valuation output.
    /// </summary>
    /// <returns>A task that completes after setup and correctness verification.</returns>
    /// <exception cref="AggregateException">Thrown when setup and its cleanup both fail.</exception>
    [GlobalSetup]
    public async Task GlobalSetupAsync()
    {
        try
        {
            _database = new BenchmarkDatabaseFixture();
            await _database.InitializeAsync();
            if (AfterDatabaseInitializedForTest is not null)
            {
                await AfterDatabaseInitializedForTest(_database);
            }

            _workload = SyntheticPortfolioWorkload.Create(InstrumentCount);
            await SyntheticPortfolioWorkload.SeedAsync(_database.BenchmarkConnectionString, _workload);
            _serviceProvider = BenchmarkServiceProviderFactory.Create(_database.BenchmarkConnectionString);

            FinCore.Application.Portfolios.PortfolioValuation valuation =
                await BenchmarkServiceProviderFactory.ValueAsync(_serviceProvider, _workload);
            SyntheticPortfolioWorkload.ValidateValuation(_workload, valuation);
        }
        catch (Exception setupException)
        {
            try
            {
                await GlobalCleanupAsync();
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Benchmark setup and owned-database cleanup both failed.", setupException, cleanupException);
            }

            throw;
        }
    }

    /// <summary>
    /// Measures one warm application-service valuation using the existing sequential SQL transaction flow.
    /// </summary>
    /// <returns>A task completed after one valuation result has been checked.</returns>
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
    /// Drops only the run-owned database after the BenchmarkDotNet worker finishes.
    /// </summary>
    /// <returns>A task that completes after safe cleanup.</returns>
    [GlobalCleanup]
    public async Task GlobalCleanupAsync()
    {
        Exception? serviceFailure = null;
        try
        {
            if (_serviceProvider is not null)
            {
                await _serviceProvider.DisposeAsync();
                _serviceProvider = null;
            }
        }
        catch (Exception exception)
        {
            serviceFailure = exception;
        }

        try
        {
            if (_database is not null)
            {
                await _database.DisposeAsync();
                _database = null;
            }
        }
        catch (Exception exception) when (serviceFailure is not null)
        {
            throw new AggregateException("Service and owned-database cleanup both failed.", serviceFailure, exception);
        }

        if (serviceFailure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(serviceFailure).Throw();
        }
    }
}
