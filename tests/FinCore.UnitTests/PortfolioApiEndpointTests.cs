using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FinCore.Api.Models.Instruments;
using FinCore.Api.Models.Portfolios;
using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace FinCore.UnitTests;

/// <summary>
/// Exercises portfolio and trading endpoints over an in-memory HTTP host.
/// </summary>
public sealed class PortfolioApiEndpointTests
{
    /// <summary>
    /// Verifies portfolio creation returns a retrievable resource location.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task CreatePortfolio_ReturnsCreatedWithLocation()
    {
        using ApiHost host = new();

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            "/api/portfolios",
            new { name = " Synthetic Growth ", baseCurrency = "usd" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/portfolios/{ApiHost.PortfolioId}", response.Headers.Location?.AbsolutePath);
        PortfolioResponse? portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>();
        Assert.NotNull(portfolio);
        Assert.Equal("Synthetic Growth", portfolio.Name);
        Assert.Equal("USD", portfolio.BaseCurrency);
    }

    /// <summary>
    /// Verifies portfolio retrieval, listing, and derived positions use the Application services.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task PortfolioReads_ReturnSuccessfulRepresentations()
    {
        using ApiHost host = new();

        using HttpResponseMessage getResponse =
            await host.Client.GetAsync($"/api/portfolios/{ApiHost.PortfolioId}");
        using HttpResponseMessage listResponse = await host.Client.GetAsync("/api/portfolios");
        using HttpResponseMessage positionsResponse =
            await host.Client.GetAsync($"/api/portfolios/{ApiHost.PortfolioId}/positions");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, positionsResponse.StatusCode);
        PositionResponse[] positions =
            await positionsResponse.Content.ReadFromJsonAsync<PositionResponse[]>() ?? [];
        Assert.Empty(positions);
    }

    /// <summary>
    /// Verifies unsupported currency, malformed identifiers, and unrepresentable values return validation errors.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task InvalidRequests_ReturnValidationProblemDetails()
    {
        using ApiHost host = new();

        using HttpResponseMessage currencyResponse = await host.Client.PostAsJsonAsync(
            "/api/portfolios",
            new { name = "Synthetic", baseCurrency = "EUR" });
        using HttpResponseMessage idResponse = await host.Client.GetAsync("/api/portfolios/not-a-guid");
        using HttpResponseMessage tradeResponse = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = "Buy",
                quantity = 0.00000000001m,
                unitPrice = 10m,
                executedAt = ApiHost.Start
            });
        using HttpResponseMessage unsupportedSideResponse = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = "Hold",
                quantity = 1m,
                unitPrice = 10m,
                executedAt = ApiHost.Start
            });
        using HttpResponseMessage numericSideResponse = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = 1,
                quantity = 1m,
                unitPrice = 10m,
                executedAt = ApiHost.Start
            });
        using HttpResponseMessage nonUtcResponse = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = "Buy",
                quantity = 1m,
                unitPrice = 10m,
                executedAt = new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.FromHours(-5))
            });

        Assert.Equal(HttpStatusCode.BadRequest, currencyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, idResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tradeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unsupportedSideResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, numericSideResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nonUtcResponse.StatusCode);
        Assert.Contains(
            "application/problem+json",
            currencyResponse.Content.Headers.ContentType?.MediaType,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies missing resources and expected business conflicts map to 404 and 409.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task MissingResourcesAndBusinessConflicts_ReturnProblemDetails()
    {
        using ApiHost host = new();
        host.Portfolios.GetPortfolioResult = null;
        host.Instruments.RegisterException = new InstrumentSymbolAlreadyRegisteredException("MSFT");
        host.Trades.RecordException = new TradeIdempotencyConflictException(Guid.NewGuid());

        using HttpResponseMessage missingResponse =
            await host.Client.GetAsync($"/api/portfolios/{Guid.NewGuid()}");
        using HttpResponseMessage duplicateResponse = await host.Client.PostAsJsonAsync(
            "/api/instruments",
            new { symbol = "msft", name = "Example", currency = "USD" });
        using HttpResponseMessage conflictResponse = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = "Buy",
                quantity = 2m,
                unitPrice = 10m,
                executedAt = ApiHost.Start
            });

        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal(
            "application/problem+json",
            duplicateResponse.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Verifies an instrument with no eligible price returns a not-found Problem Details response.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task LatestPrice_WhenNoEligiblePrice_ReturnsNotFound()
    {
        using ApiHost host = new();

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/instruments/{ApiHost.InstrumentId}/prices/latest?asOfUtc={Uri.EscapeDataString(ApiHost.Start.ToString("O"))}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// Verifies valuation returns the Application result and supports the optional UTC cutoff.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_ReturnsValuationAndForwardsCutoff()
    {
        using ApiHost host = new();
        host.Valuations.Result = new PortfolioValuation(
            ApiHost.PortfolioId,
            "USD",
            ApiHost.Start,
            [
                new PortfolioValuationPosition(
                    ApiHost.InstrumentId,
                    "MSFT",
                    80m,
                    450m,
                    ApiHost.Start.AddMinutes(-1),
                    36_000m)
            ],
            36_000m);

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation?asOfUtc={Uri.EscapeDataString(ApiHost.Start.ToString("O"))}");
        PortfolioValuationResponse? valuation =
            await response.Content.ReadFromJsonAsync<PortfolioValuationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(valuation);
        Assert.Equal(36_000m, valuation.TotalMarketValue);
        Assert.Equal(80m, Assert.Single(valuation.Positions).Quantity);
        Assert.Equal(ApiHost.Start, host.Valuations.LastCutoff);
    }

    /// <summary>
    /// Verifies missing, malformed, and non-UTC valuation cutoffs return validation errors.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_InvalidCutoffs_ReturnBadRequest()
    {
        using ApiHost host = new();

        using HttpResponseMessage malformed = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation?asOfUtc=not-a-date");
        using HttpResponseMessage nonUtc = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation?asOfUtc=2026-11-16T10%3A00%3A00-05%3A00");

        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nonUtc.StatusCode);
    }

    /// <summary>
    /// Verifies a missing portfolio returns 404 for valuation requests.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_MissingPortfolio_ReturnsNotFound()
    {
        using ApiHost host = new();
        host.Valuations.Exception = new PortfolioNotFoundException(Guid.NewGuid());

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Verifies missing eligible prices return all affected instruments without a partial total.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_MissingPrices_ReturnsConflictDetails()
    {
        using ApiHost host = new();
        host.Valuations.Exception = new MissingMarketPricesException(
            ApiHost.PortfolioId,
            ApiHost.Start,
            [new MissingMarketPriceInstrument(ApiHost.InstrumentId, "MSFT")]);

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            ApiHost.InstrumentId.ToString(),
            body.RootElement.GetProperty("missingInstruments")[0].GetProperty("instrumentId").GetString());
        Assert.False(body.RootElement.TryGetProperty("totalMarketValue", out _));
    }

    /// <summary>
    /// Verifies checked valuation overflow maps to a sanitized 422 response.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_Overflow_ReturnsUnprocessableEntity()
    {
        using ApiHost host = new();
        host.Valuations.Exception = new PortfolioValuationOverflowException(
            ApiHost.PortfolioId,
            ApiHost.Start,
            new OverflowException("private numeric details"));

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain("private numeric details", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies a portfolio with no open positions returns an empty valuation and zero total.
    /// </summary>
    [Fact]
    public async Task PortfolioValuation_EmptyPositions_ReturnsZeroTotal()
    {
        using ApiHost host = new();
        host.Valuations.Result = new PortfolioValuation(
            ApiHost.PortfolioId,
            "USD",
            ApiHost.Start,
            [],
            0m);

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation");
        PortfolioValuationResponse? valuation =
            await response.Content.ReadFromJsonAsync<PortfolioValuationResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(valuation);
        Assert.Empty(valuation.Positions);
        Assert.Equal(0m, valuation.TotalMarketValue);
    }

    /// <summary>
    /// Verifies the Development OpenAPI document contains all eight portfolio and instrument operations.
    /// </summary>
    /// <returns>A task that completes when the OpenAPI assertions finish.</returns>
    [Fact]
    public async Task OpenApiDocument_ContainsPortfolioAndInstrumentRoutes()
    {
        using ApiHost host = new();

        using HttpResponseMessage response = await host.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pathElements = document.RootElement.GetProperty("paths");
        HashSet<string> paths = pathElements
            .EnumerateObject()
            .Select(path => path.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("/api/portfolios", paths);
        Assert.True(pathElements.GetProperty("/api/portfolios").TryGetProperty("post", out _));
        Assert.True(pathElements.GetProperty("/api/portfolios").TryGetProperty("get", out _));
        Assert.Contains("/api/portfolios/{id}", paths);
        Assert.Contains("/api/portfolios/{id}/positions", paths);
        Assert.Contains("/api/portfolios/{id}/valuation", paths);
        Assert.Contains("/api/portfolios/{portfolioId}/trades", paths);
        Assert.Contains("/api/instruments", paths);
        Assert.Contains("/api/instruments/{id}/prices", paths);
        Assert.Contains("/api/instruments/{id}/prices/latest", paths);
    }

    /// <summary>
    /// Verifies deadlocks produce retryable 503 responses without exposing SQL details.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task TradeDeadlock_ReturnsRetryableSanitizedServiceUnavailable()
    {
        using ApiHost host = new();
        host.Trades.RecordException =
            new TransactionDeadlockException(new InvalidOperationException("private SQL details"));

        using HttpResponseMessage response = await host.Client.PostAsJsonAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/trades",
            new
            {
                tradeId = Guid.NewGuid(),
                instrumentId = ApiHost.InstrumentId,
                side = "Buy",
                quantity = 2m,
                unitPrice = 10m,
                executedAt = ApiHost.Start
            });

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("2", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.DoesNotContain("private SQL details", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies valuation query timeouts return a retryable service response without internal details.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task TransientPersistenceFailure_ReturnsSanitizedServiceUnavailable()
    {
        InvalidOperationException providerException = new("private SQL connection details");
        Assert.IsType<TransientPersistenceException>(
            SqlExceptionTranslator.Translate(258, 20, providerException, CancellationToken.None));
        Assert.Same(
            providerException,
            SqlExceptionTranslator.Translate(258, 19, providerException, CancellationToken.None));
        Assert.Same(
            providerException,
            SqlExceptionTranslator.Translate(18456, 14, providerException, CancellationToken.None));
        Assert.Same(
            providerException,
            SqlExceptionTranslator.Translate(102, 15, providerException, CancellationToken.None));
        Assert.IsType<TransactionDeadlockException>(
            SqlExceptionTranslator.Translate(1205, 20, providerException, CancellationToken.None));
        Assert.IsType<TimeoutException>(
            SqlExceptionTranslator.Translate(-2, 20, providerException, CancellationToken.None));
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        Assert.IsType<OperationCanceledException>(
            SqlExceptionTranslator.Translate(-2, 20, providerException, canceled.Token));
        Assert.IsType<OperationCanceledException>(
            SqlExceptionTranslator.Translate(258, 20, providerException, canceled.Token));

        using ApiHost host = new();
        host.Valuations.Exception = new TransientPersistenceException(providerException);

        using HttpResponseMessage response = await host.Client.GetAsync(
            $"/api/portfolios/{ApiHost.PortfolioId}/valuation");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("2", response.Headers.RetryAfter?.Delta?.TotalSeconds.ToString());
        Assert.Contains("transient persistence failure", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private SQL connection details", body, StringComparison.Ordinal);
        Assert.DoesNotContain("tradeId", body, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies unexpected failures return a generic server error without internal exception details.
    /// </summary>
    /// <returns>A task that completes when the HTTP response assertions finish.</returns>
    [Fact]
    public async Task UnexpectedFailure_ReturnsSanitizedProblemDetails()
    {
        using ApiHost host = new();
        host.Portfolios.ListException = new InvalidOperationException("private database detail");

        using HttpResponseMessage response = await host.Client.GetAsync("/api/portfolios");
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private database detail", body, StringComparison.Ordinal);
        Assert.Contains("Internal server error", body, StringComparison.Ordinal);
    }

    internal sealed class ApiHost : IDisposable
    {
        internal static readonly Guid PortfolioId = Guid.Parse("a23cf908-0812-4c5f-9f31-cf5f76d69e1f");
        internal static readonly Guid InstrumentId = Guid.Parse("bc74902a-ff1e-462e-9fdd-317204fe33d9");
        internal static readonly DateTimeOffset Start =
            new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

        private readonly WebApplicationFactory<Program> _factory;

        internal ApiHost()
        {
            Portfolios = new FakePortfolioApplicationService();
            Instruments = new FakeInstrumentApplicationService();
            Trades = new FakeTradeApplicationService();
            MarketPrices = new FakeMarketPriceApplicationService();
            Valuations = new FakePortfolioValuationApplicationService();

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureTestServices(services =>
                    {
                        services.AddSingleton<IPortfolioApplicationService>(Portfolios);
                        services.AddSingleton<IInstrumentApplicationService>(Instruments);
                        services.AddSingleton<ITradeApplicationService>(Trades);
                        services.AddSingleton<IMarketPriceApplicationService>(MarketPrices);
                        services.AddSingleton<IPortfolioValuationApplicationService>(Valuations);
                    });
                });
            Client = _factory.CreateClient();
        }

        internal HttpClient Client { get; }

        internal FakePortfolioApplicationService Portfolios { get; }

        internal FakeInstrumentApplicationService Instruments { get; }

        internal FakeTradeApplicationService Trades { get; }

        internal FakeMarketPriceApplicationService MarketPrices { get; }

        internal FakePortfolioValuationApplicationService Valuations { get; }

        /// <summary>
        /// Disposes the in-memory HTTP client and test host.
        /// </summary>
        public void Dispose()
        {
            Client.Dispose();
            _factory.Dispose();
        }

        internal sealed class FakePortfolioApplicationService : IPortfolioApplicationService
        {
            private readonly Portfolio _portfolio =
                new(PortfolioId, "Synthetic Growth", "USD", Start);

            internal Portfolio? GetPortfolioResult { get; set; } = new(
                PortfolioId,
                "Synthetic Growth",
                "USD",
                Start);

            internal Exception? ListException { get; set; }

            /// <inheritdoc />
            public Task<Portfolio> CreatePortfolioAsync(string name, CancellationToken cancellationToken)
            {
                return Task.FromResult(new Portfolio(PortfolioId, name, "USD", Start));
            }

            /// <inheritdoc />
            public Task<Portfolio?> GetPortfolioAsync(Guid portfolioId, CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    portfolioId == PortfolioId ? GetPortfolioResult : null);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<Portfolio>> ListPortfoliosAsync(CancellationToken cancellationToken)
            {
                return ListException is null
                    ? Task.FromResult<IReadOnlyList<Portfolio>>([_portfolio])
                    : Task.FromException<IReadOnlyList<Portfolio>>(ListException);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<Position>> GetPositionsAsync(
                Guid portfolioId,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<Position>>([]);
            }
        }

        internal sealed class FakeInstrumentApplicationService : IInstrumentApplicationService
        {
            internal Exception? RegisterException { get; set; }

            /// <inheritdoc />
            public Task<Instrument> RegisterEquityAsync(
                string symbol,
                string name,
                CancellationToken cancellationToken)
            {
                return RegisterException is null
                    ? Task.FromResult(new Instrument(
                        Guid.NewGuid(),
                        symbol,
                        name,
                        "USD",
                        InstrumentType.Equity))
                    : Task.FromException<Instrument>(RegisterException);
            }
        }

        internal sealed class FakeTradeApplicationService : ITradeApplicationService
        {
            internal Exception? RecordException { get; set; }

            /// <inheritdoc />
            public Task<Trade> RecordTradeAsync(
                Guid tradeId,
                Guid portfolioId,
                Guid instrumentId,
                TradeSide side,
                decimal quantity,
                decimal unitPrice,
                DateTimeOffset executedAt,
                CancellationToken cancellationToken)
            {
                return RecordException is null
                    ? Task.FromResult(new Trade(
                        tradeId,
                        portfolioId,
                        instrumentId,
                        side,
                        quantity,
                        unitPrice,
                        executedAt))
                    : Task.FromException<Trade>(RecordException);
            }
        }

        internal sealed class FakeMarketPriceApplicationService : IMarketPriceApplicationService
        {
            internal MarketPrice? LatestPrice { get; set; }

            /// <inheritdoc />
            public Task<MarketPrice> RecordPriceAsync(
                Guid instrumentId,
                decimal price,
                DateTimeOffset asOfUtc,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(
                    new MarketPrice(Guid.NewGuid(), instrumentId, price, "USD", asOfUtc));
            }

            /// <inheritdoc />
            public Task<MarketPrice?> GetLatestEligiblePriceAsync(
                Guid instrumentId,
                DateTimeOffset asOfUtc,
                CancellationToken cancellationToken)
            {
                return Task.FromResult(LatestPrice);
            }
        }

        internal sealed class FakePortfolioValuationApplicationService : IPortfolioValuationApplicationService
        {
            internal PortfolioValuation Result { get; set; } =
                new(PortfolioId, "USD", Start, [], 0m);

            internal Exception? Exception { get; set; }

            internal DateTimeOffset? LastCutoff { get; private set; }

            /// <inheritdoc />
            public Task<PortfolioValuation> GetPortfolioValuationAsync(
                Guid portfolioId,
                DateTimeOffset? asOfUtc,
                CancellationToken cancellationToken)
            {
                LastCutoff = asOfUtc;
                cancellationToken.ThrowIfCancellationRequested();
                return Exception is null
                    ? Task.FromResult(Result)
                    : Task.FromException<PortfolioValuation>(Exception);
            }
        }
    }
}
