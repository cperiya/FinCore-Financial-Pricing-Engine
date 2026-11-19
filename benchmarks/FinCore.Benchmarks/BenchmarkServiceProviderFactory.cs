using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;
using FinCore.Infrastructure.DependencyInjection;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.Benchmarks;

internal static class BenchmarkServiceProviderFactory
{
    public static ServiceProvider Create(
        string connectionString,
        DbCommandInterceptor? interceptor = null,
        IPortfolioValuationDiagnostics? diagnostics = null,
        bool useJsonCollectionParameter = false)
    {
        ServiceCollection services = new();
        services.AddSingleton<PositionCalculationService>();
        services.AddSingleton<StockValuationService>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddFinCoreInfrastructure(connectionString);
        if (useJsonCollectionParameter)
        {
            services.Configure<PortfolioCollectionParameterOptions>(
                options => options.UseJsonParameterForIds = true);
        }

        services.AddScoped<IPortfolioValuationApplicationService, PortfolioValuationApplicationService>();
        if (diagnostics is not null)
        {
            services.AddSingleton(diagnostics);
            services.AddSingleton<IPortfolioValuationDiagnostics>(diagnostics);
        }

        if (interceptor is not null)
        {
            services.AddDbContext<FinCoreDbContext>(options => options.AddInterceptors(interceptor));
        }

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    public static async Task<FinCore.Application.Portfolios.PortfolioValuation> ValueAsync(
        ServiceProvider serviceProvider,
        SyntheticWorkload workload)
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
