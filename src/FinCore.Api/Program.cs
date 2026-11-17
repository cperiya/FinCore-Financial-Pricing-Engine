using FinCore.Application.Portfolios;
using FinCore.Application.Pricing;
using FinCore.Api.Middleware;
using FinCore.Domain.Portfolios;
using FinCore.Domain.Valuation;
using FinCore.Infrastructure.DependencyInjection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddSingleton<StockValuationService>();
builder.Services.AddSingleton<PositionCalculationService>();
builder.Services.AddScoped<IStockPricingService, StockPricingService>();

string? persistenceConnectionString = builder.Configuration.GetConnectionString("FinCore");
if (!string.IsNullOrWhiteSpace(persistenceConnectionString))
{
    builder.Services.AddFinCoreInfrastructure(persistenceConnectionString);
    builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
    builder.Services.AddScoped<IPortfolioApplicationService, PortfolioApplicationService>();
    builder.Services.AddScoped<IInstrumentApplicationService, InstrumentApplicationService>();
    builder.Services.AddScoped<ITradeApplicationService, TradeApplicationService>();
    builder.Services.AddScoped<IMarketPriceApplicationService, MarketPriceApplicationService>();
    builder.Services.AddScoped<IPortfolioValuationApplicationService, PortfolioValuationApplicationService>();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

/// <summary>
/// Exposes the top-level API entry point to integration-test hosts.
/// </summary>
public partial class Program
{
}
