using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinCore.UnitTests;

/// <summary>
/// Verifies stock pricing request validation and response mapping over HTTP.
/// </summary>
public sealed class StockPricingEndpointTests
{
    /// <summary>
    /// Verifies that a missing symbol is rejected.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertion finishes.</returns>
    [Fact]
    public async Task GetStock_WithoutSymbol_ReturnsBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/pricing/stock?unitPrice=450&quantity=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that a blank symbol is rejected.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertion finishes.</returns>
    [Fact]
    public async Task GetStock_WithBlankSymbol_ReturnsBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/pricing/stock?symbol=%20%20&unitPrice=450&quantity=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that a negative unit price is rejected.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertion finishes.</returns>
    [Fact]
    public async Task GetStock_WithNegativePrice_ReturnsBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/pricing/stock?symbol=MSFT&unitPrice=-450&quantity=100");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that a zero quantity is rejected.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertion finishes.</returns>
    [Fact]
    public async Task GetStock_WithZeroQuantity_ReturnsBadRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/pricing/stock?symbol=MSFT&unitPrice=450&quantity=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Verifies that valid input returns the uppercased symbol and calculated market value.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task GetStock_WithValidInput_ReturnsNormalizedSymbolAndMarketValue()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response =
            await client.GetAsync("/api/pricing/stock?symbol=msft&unitPrice=450&quantity=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement body = document.RootElement;

        Assert.Equal("MSFT", body.GetProperty("symbol").GetString());
        Assert.Equal(450m, body.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(100m, body.GetProperty("quantity").GetDecimal());
        Assert.Equal(45000m, body.GetProperty("marketValue").GetDecimal());
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
    }
}
