using FinCore.Application.Pricing;
using FinCore.Domain.Valuation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<StockValuationService>();
builder.Services.AddScoped<IStockPricingService, StockPricingService>();

var app = builder.Build();

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
