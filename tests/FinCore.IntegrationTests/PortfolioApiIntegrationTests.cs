using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinCore.Api.Models.Instruments;
using FinCore.Api.Models.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.IntegrationTests;

/// <summary>
/// Exercises portfolio and trade HTTP workflows against the fixture-owned migrated SQL Server database.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerIntegrationCollection.Name)]
public sealed class PortfolioApiIntegrationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SqlServerTestDatabaseFixture _fixture;

    /// <summary>
    /// Initializes HTTP integration tests with the isolated SQL Server fixture.
    /// </summary>
    /// <param name="fixture">The SQL Server database owned by the current test run.</param>
    public PortfolioApiIntegrationTests(SqlServerTestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies portfolio, instrument, trade, position, and market-price endpoints against SQL Server.
    /// </summary>
    /// <returns>A task that completes when the HTTP and persistence assertions finish.</returns>
    [SqlServerFact]
    public async Task PortfolioApi_ExecutesCompletePortfolioWorkflow()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        AssertUsesFixtureDatabase(factory);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage createPortfolioResponse = await client.PostAsJsonAsync(
            "/api/portfolios",
            new CreatePortfolioRequest { Name = "Synthetic HTTP Portfolio", BaseCurrency = "USD" });
        Assert.Equal(HttpStatusCode.Created, createPortfolioResponse.StatusCode);
        Assert.NotNull(createPortfolioResponse.Headers.Location);
        PortfolioResponse portfolio = Assert.IsType<PortfolioResponse>(
            await createPortfolioResponse.Content.ReadFromJsonAsync<PortfolioResponse>());

        using HttpResponseMessage listResponse = await client.GetAsync("/api/portfolios");
        PortfolioResponse[] portfolios = Assert.IsType<PortfolioResponse[]>(
            await listResponse.Content.ReadFromJsonAsync<PortfolioResponse[]>());
        Assert.Contains(portfolios, item => item.Id == portfolio.Id);

        using HttpResponseMessage getResponse = await client.GetAsync($"/api/portfolios/{portfolio.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using HttpResponseMessage registerResponse = await client.PostAsJsonAsync(
            "/api/instruments",
            new RegisterInstrumentRequest
            {
                Symbol = $"f{Guid.NewGuid():N}"[..12],
                Name = "Synthetic HTTP Equity",
                Currency = "USD"
            });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        InstrumentResponse instrument = Assert.IsType<InstrumentResponse>(
            await registerResponse.Content.ReadFromJsonAsync<InstrumentResponse>(JsonOptions));
        Assert.Equal(instrument.Symbol.ToUpperInvariant(), instrument.Symbol);

        using HttpResponseMessage recordPriceResponse = await client.PostAsJsonAsync(
            $"/api/instruments/{instrument.Id}/prices",
            new RecordMarketPriceRequest
            {
                Price = 25.75m,
                Currency = "USD",
                AsOfUtc = Start
            });
        Assert.Equal(HttpStatusCode.OK, recordPriceResponse.StatusCode);

        using HttpResponseMessage latestPriceResponse = await client.GetAsync(
            $"/api/instruments/{instrument.Id}/prices/latest?asOfUtc={Uri.EscapeDataString(Start.AddMinutes(1).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, latestPriceResponse.StatusCode);
        MarketPriceResponse price = Assert.IsType<MarketPriceResponse>(
            await latestPriceResponse.Content.ReadFromJsonAsync<MarketPriceResponse>());
        Assert.Equal(25.75m, price.Price);

        RecordTradeRequest buy = new()
        {
            TradeId = Guid.NewGuid(),
            InstrumentId = instrument.Id,
            Side = TradeSide.Buy,
            Quantity = 10m,
            UnitPrice = 24m,
            ExecutedAt = Start
        };
        using HttpResponseMessage buyResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            buy,
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, buyResponse.StatusCode);
        TradeResponse recordedTrade = Assert.IsType<TradeResponse>(
            await buyResponse.Content.ReadFromJsonAsync<TradeResponse>(JsonOptions));
        Assert.Equal(buy.TradeId, recordedTrade.Id);

        using HttpResponseMessage positionsResponse =
            await client.GetAsync($"/api/portfolios/{portfolio.Id}/positions");
        Assert.Equal(HttpStatusCode.OK, positionsResponse.StatusCode);
        PositionResponse[] positions = Assert.IsType<PositionResponse[]>(
            await positionsResponse.Content.ReadFromJsonAsync<PositionResponse[]>());
        Assert.Equal(10m, Assert.Single(positions).Quantity);
    }

    /// <summary>
    /// Verifies exact trade retries replay, conflicting duplicate IDs and oversells return conflicts.
    /// </summary>
    /// <returns>A task that completes when the HTTP and persistence assertions finish.</returns>
    [SqlServerFact]
    public async Task TradeApi_HandlesReplayConflictAndOverselling()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        AssertUsesFixtureDatabase(factory);
        using HttpClient client = factory.CreateClient();
        PortfolioResponse portfolio = await CreatePortfolioAsync(client);
        InstrumentResponse instrument = await RegisterInstrumentAsync(client);
        RecordTradeRequest buy = new()
        {
            TradeId = Guid.NewGuid(),
            InstrumentId = instrument.Id,
            Side = TradeSide.Buy,
            Quantity = 10m,
            UnitPrice = 20m,
            ExecutedAt = Start
        };

        using HttpResponseMessage firstResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            buy,
            JsonOptions);
        using HttpResponseMessage replayResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            buy with { Quantity = 10.00m, UnitPrice = 20.00m },
            JsonOptions);
        using HttpResponseMessage conflictingResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            buy with { Quantity = 9m },
            JsonOptions);

        RecordTradeRequest sell = new()
        {
            TradeId = Guid.NewGuid(),
            InstrumentId = instrument.Id,
            Side = TradeSide.Sell,
            Quantity = 11m,
            UnitPrice = 21m,
            ExecutedAt = Start.AddMinutes(1)
        };
        using HttpResponseMessage oversellResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            sell,
            JsonOptions);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflictingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, oversellResponse.StatusCode);
        TradeResponse original = Assert.IsType<TradeResponse>(
            await firstResponse.Content.ReadFromJsonAsync<TradeResponse>(JsonOptions));
        TradeResponse replay = Assert.IsType<TradeResponse>(
            await replayResponse.Content.ReadFromJsonAsync<TradeResponse>(JsonOptions));
        Assert.Equal(original, replay);
    }

    /// <summary>
    /// Verifies concurrent HTTP sells serialize so only one seven-share sell can consume ten shares.
    /// </summary>
    /// <returns>A task that completes when the HTTP and persistence assertions finish.</returns>
    [SqlServerFact]
    public async Task TradeApi_ConcurrentSellsAllowExactlyOneRequest()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        AssertUsesFixtureDatabase(factory);
        using HttpClient client = factory.CreateClient();
        PortfolioResponse portfolio = await CreatePortfolioAsync(client);
        InstrumentResponse instrument = await RegisterInstrumentAsync(client);

        using HttpResponseMessage buyResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            new RecordTradeRequest
            {
                TradeId = Guid.NewGuid(),
                InstrumentId = instrument.Id,
                Side = TradeSide.Buy,
                Quantity = 10m,
                UnitPrice = 20m,
                ExecutedAt = Start
            },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, buyResponse.StatusCode);

        RecordTradeRequest firstSell = CreateSell(instrument.Id);
        RecordTradeRequest secondSell = CreateSell(instrument.Id);
        Task<HttpResponseMessage> firstRequest = client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            firstSell,
            JsonOptions);
        Task<HttpResponseMessage> secondRequest = client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            secondSell,
            JsonOptions);
        HttpResponseMessage[] outcomes = await Task.WhenAll(firstRequest, secondRequest);
        try
        {
            Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(outcomes, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            foreach (HttpResponseMessage outcome in outcomes)
            {
                outcome.Dispose();
            }
        }
    }

    /// <summary>
    /// Verifies the approved 100-share buy, 20-share sale, and USD 450 valuation flow over HTTP and SQL Server.
    /// </summary>
    [SqlServerFact]
    public async Task PortfolioApi_ValuesRemainingSharesAtSimulatedUsdPrice()
    {
        using WebApplicationFactory<Program> factory = CreateFactory();
        AssertUsesFixtureDatabase(factory);
        using HttpClient client = factory.CreateClient();
        PortfolioResponse portfolio = await CreatePortfolioAsync(client);
        InstrumentResponse instrument = await RegisterInstrumentAsync(client);

        using HttpResponseMessage buyResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            new RecordTradeRequest
            {
                TradeId = Guid.NewGuid(),
                InstrumentId = instrument.Id,
                Side = TradeSide.Buy,
                Quantity = 100m,
                UnitPrice = 400m,
                ExecutedAt = Start
            },
            JsonOptions);
        using HttpResponseMessage sellResponse = await client.PostAsJsonAsync(
            $"/api/portfolios/{portfolio.Id}/trades",
            new RecordTradeRequest
            {
                TradeId = Guid.NewGuid(),
                InstrumentId = instrument.Id,
                Side = TradeSide.Sell,
                Quantity = 20m,
                UnitPrice = 410m,
                ExecutedAt = Start.AddMinutes(1)
            },
            JsonOptions);
        using HttpResponseMessage priceResponse = await client.PostAsJsonAsync(
            $"/api/instruments/{instrument.Id}/prices",
            new RecordMarketPriceRequest
            {
                Price = 450m,
                Currency = "USD",
                AsOfUtc = Start
            });

        Assert.Equal(HttpStatusCode.OK, buyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, sellResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, priceResponse.StatusCode);

        using HttpResponseMessage valuationResponse = await client.GetAsync(
            $"/api/portfolios/{portfolio.Id}/valuation?asOfUtc={Uri.EscapeDataString(Start.AddMinutes(2).ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, valuationResponse.StatusCode);
        PortfolioValuationResponse valuation = Assert.IsType<PortfolioValuationResponse>(
            await valuationResponse.Content.ReadFromJsonAsync<PortfolioValuationResponse>());

        PortfolioValuationPositionResponse position = Assert.Single(valuation.Positions);
        Assert.Equal("USD", valuation.Currency);
        Assert.Equal(80m, position.Quantity);
        Assert.Equal(450m, position.MarketPrice);
        Assert.Equal(36_000m, position.MarketValue);
        Assert.Equal(36_000m, valuation.TotalMarketValue);
    }

    private WebApplicationFactory<Program> CreateFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:FinCore", _fixture.TestConnectionString);
            });
    }

    private static async Task<PortfolioResponse> CreatePortfolioAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/portfolios",
            new CreatePortfolioRequest { Name = "Synthetic Trade Portfolio", BaseCurrency = "USD" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<PortfolioResponse>(
            await response.Content.ReadFromJsonAsync<PortfolioResponse>());
    }

    private static async Task<InstrumentResponse> RegisterInstrumentAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/instruments",
            new RegisterInstrumentRequest
            {
                Symbol = $"f{Guid.NewGuid():N}"[..12],
                Name = "Synthetic Trade Equity",
                Currency = "USD"
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<InstrumentResponse>(
            await response.Content.ReadFromJsonAsync<InstrumentResponse>(JsonOptions));
    }

    private static RecordTradeRequest CreateSell(Guid instrumentId)
    {
        return new RecordTradeRequest
        {
            TradeId = Guid.NewGuid(),
            InstrumentId = instrumentId,
            Side = TradeSide.Sell,
            Quantity = 7m,
            UnitPrice = 21m,
            ExecutedAt = Start.AddMinutes(1)
        };
    }

    private void AssertUsesFixtureDatabase(WebApplicationFactory<Program> factory)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        FinCoreDbContext dbContext = scope.ServiceProvider.GetRequiredService<FinCoreDbContext>();
        Assert.Equal(_fixture.DatabaseName, dbContext.Database.GetDbConnection().Database);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
