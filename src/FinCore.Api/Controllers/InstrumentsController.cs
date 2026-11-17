using FinCore.Api.Models;
using FinCore.Api.Models.Instruments;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

/// <summary>
/// HTTP endpoints for equity instruments and simulated market prices.
/// </summary>
[ApiController]
[Route("api/instruments")]
public sealed class InstrumentsController : ControllerBase
{
    private readonly IInstrumentApplicationService _instrumentApplicationService;
    private readonly IMarketPriceApplicationService _marketPriceApplicationService;

    /// <summary>
    /// Initializes a controller that delegates instrument and price workflows to Application services.
    /// </summary>
    /// <param name="instrumentApplicationService">The instrument use-case service.</param>
    /// <param name="marketPriceApplicationService">The market-price use-case service.</param>
    public InstrumentsController(
        IInstrumentApplicationService instrumentApplicationService,
        IMarketPriceApplicationService marketPriceApplicationService)
    {
        ArgumentNullException.ThrowIfNull(instrumentApplicationService);
        ArgumentNullException.ThrowIfNull(marketPriceApplicationService);
        _instrumentApplicationService = instrumentApplicationService;
        _marketPriceApplicationService = marketPriceApplicationService;
    }

    /// <summary>
    /// Registers a USD equity after normalizing its symbol.
    /// </summary>
    /// <param name="request">The validated equity details.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The registered instrument.</returns>
    [HttpPost]
    [EndpointSummary("Register a USD equity")]
    [EndpointDescription("Registers an equity and normalizes its symbol before persistence.")]
    [ProducesResponseType(typeof(InstrumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InstrumentResponse>> Register(
        RegisterInstrumentRequest request,
        CancellationToken cancellationToken)
    {
        Instrument instrument = await _instrumentApplicationService.RegisterEquityAsync(
            request.Symbol!,
            request.Name!,
            cancellationToken);

        return Ok(ToResponse(instrument));
    }

    /// <summary>
    /// Records a simulated USD price for an instrument.
    /// </summary>
    /// <param name="id">The instrument identifier.</param>
    /// <param name="request">The validated price and UTC observation time.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The stored market price.</returns>
    [HttpPost("{id}/prices")]
    [EndpointSummary("Record a simulated market price")]
    [EndpointDescription("Stores a positive USD price with a UTC observation time.")]
    [ProducesResponseType(typeof(MarketPriceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarketPriceResponse>> RecordPrice(
        Guid id,
        RecordMarketPriceRequest request,
        CancellationToken cancellationToken)
    {
        MarketPrice marketPrice = await _marketPriceApplicationService.RecordPriceAsync(
            id,
            request.Price!.Value,
            request.AsOfUtc!.Value,
            cancellationToken);

        return Ok(ToResponse(marketPrice));
    }

    /// <summary>
    /// Retrieves the latest price eligible at a required UTC cutoff.
    /// </summary>
    /// <param name="id">The instrument identifier.</param>
    /// <param name="asOfUtc">The inclusive UTC cutoff for eligible prices.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The latest eligible price, or a not-found Problem Details response.</returns>
    [HttpGet("{id}/prices/latest")]
    [EndpointSummary("Get the latest eligible market price")]
    [EndpointDescription("Returns the latest recorded price no later than the required UTC cutoff.")]
    [ProducesResponseType(typeof(MarketPriceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MarketPriceResponse>> GetLatestPrice(
        Guid id,
        [FromQuery] DateTimeOffset? asOfUtc,
        CancellationToken cancellationToken)
    {
        if (!ApiRequestValidation.IsUtc(asOfUtc))
        {
            ModelState.AddModelError(nameof(asOfUtc), "asOfUtc is required and must use the UTC offset.");
            return ValidationProblem(ModelState);
        }

        MarketPrice? marketPrice = await _marketPriceApplicationService.GetLatestEligiblePriceAsync(
            id,
            asOfUtc!.Value,
            cancellationToken);
        if (marketPrice is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Market price not found",
                detail: "No price is available for the instrument at the requested UTC cutoff.");
        }

        return Ok(ToResponse(marketPrice));
    }

    private static InstrumentResponse ToResponse(Instrument instrument)
    {
        return new InstrumentResponse(
            instrument.Id,
            instrument.Symbol,
            instrument.Name,
            instrument.Currency,
            instrument.InstrumentType);
    }

    private static MarketPriceResponse ToResponse(MarketPrice marketPrice)
    {
        return new MarketPriceResponse(
            marketPrice.Id,
            marketPrice.InstrumentId,
            marketPrice.Price,
            marketPrice.Currency,
            marketPrice.AsOfUtc);
    }
}
