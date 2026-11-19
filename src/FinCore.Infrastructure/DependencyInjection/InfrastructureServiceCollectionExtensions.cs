using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Infrastructure.Persistence;
using FinCore.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Infrastructure.DependencyInjection;

/// <summary>
/// Registers FinCore SQL Server persistence services.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the portfolio DbContext, repositories, and unit of work.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="connectionString">The SQL Server connection string supplied by secure configuration.</param>
    /// <returns>The same service collection for registration chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="connectionString"/> is null, empty, or whitespace.
    /// </exception>
    public static IServiceCollection AddFinCoreInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<FinCoreDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlServerOptions => sqlServerOptions.MigrationsAssembly(
                    typeof(FinCoreDbContext).Assembly.GetName().Name)));

        services.AddOptions<PortfolioCollectionParameterOptions>();
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IInstrumentRepository, InstrumentRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IMarketPriceRepository, MarketPriceRepository>();
        services.AddScoped<IUnitOfWork, FinCoreUnitOfWork>();

        return services;
    }
}
