using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace FinCore.UnitTests;

/// <summary>
/// Verifies the API root discovery endpoint through an in-memory HTTP host.
/// </summary>
public sealed class ApiHomeEndpointTests
{
    /// <summary>
    /// Verifies that the root endpoint describes FinCore and links to its resources in Development.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task GetRoot_InDevelopment_ReturnsApplicationAndAvailableLinks()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Development");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = document.RootElement;

        Assert.Equal("FinCore", root.GetProperty("applicationName").GetString());
        Assert.Equal(
            "Financial pricing API for stock position market valuation.",
            root.GetProperty("description").GetString());

        JsonElement links = root.GetProperty("links");
        Assert.Equal(
            "/api/pricing/stock?symbol={symbol}&unitPrice={unitPrice}&quantity={quantity}",
            links.GetProperty("stockValuation").GetString());
        Assert.Equal("/openapi/v1.json", links.GetProperty("openApi").GetString());
    }

    /// <summary>
    /// Verifies that the root endpoint omits the Development-only OpenAPI link in Production.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task GetRoot_InProduction_OmitsUnavailableOpenApiLink()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Production");
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement links = document.RootElement.GetProperty("links");

        Assert.True(links.TryGetProperty("stockValuation", out _));
        Assert.False(links.TryGetProperty("openApi", out _));
    }

    private static WebApplicationFactory<Program> CreateFactory(string environmentName)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environmentName));
    }
}
