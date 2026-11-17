using FinCore.Api.Models;
using FinCore.Api.Models.Portfolios;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

/// <summary>
/// HTTP endpoints for portfolio lifecycle, derived positions, valuation, and trades.
/// </summary>
[ApiController]
[Route("api/portfolios")]
public sealed class PortfoliosController : ControllerBase
{
    private readonly IPortfolioApplicationService _portfolioApplicationService;
    private readonly ITradeApplicationService _tradeApplicationService;
    private readonly IPortfolioValuationApplicationService _portfolioValuationApplicationService;

    /// <summary>
    /// Initializes a controller that delegates portfolio, trade, and valuation workflows to Application services.
    /// </summary>
    /// <param name="portfolioApplicationService">The portfolio use-case service.</param>
    /// <param name="tradeApplicationService">The trade use-case service.</param>
    /// <param name="portfolioValuationApplicationService">The portfolio valuation use-case service.</param>
    public PortfoliosController(
        IPortfolioApplicationService portfolioApplicationService,
        ITradeApplicationService tradeApplicationService,
        IPortfolioValuationApplicationService portfolioValuationApplicationService)
    {
        ArgumentNullException.ThrowIfNull(portfolioApplicationService);
        ArgumentNullException.ThrowIfNull(tradeApplicationService);
        ArgumentNullException.ThrowIfNull(portfolioValuationApplicationService);
        _portfolioApplicationService = portfolioApplicationService;
        _tradeApplicationService = tradeApplicationService;
        _portfolioValuationApplicationService = portfolioValuationApplicationService;
    }

    /// <summary>
    /// Creates a USD portfolio.
    /// </summary>
    /// <param name="request">The validated portfolio details.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The created portfolio with a location pointing to its retrieval endpoint.</returns>
    [HttpPost]
    [EndpointSummary("Create a USD portfolio")]
    [EndpointDescription("Creates a portfolio and returns a Location for retrieving it.")]
    [ProducesResponseType(typeof(PortfolioResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioResponse>> Create(
        CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        Portfolio portfolio = await _portfolioApplicationService.CreatePortfolioAsync(
            request.Name!,
            cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = portfolio.Id }, ToResponse(portfolio));
    }

    /// <summary>
    /// Lists portfolios in the stable order supplied by the Application service.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The portfolio list.</returns>
    [HttpGet]
    [EndpointSummary("List portfolios")]
    [EndpointDescription("Returns portfolios in stable creation-time and identifier order.")]
    [ProducesResponseType(typeof(IReadOnlyList<PortfolioResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PortfolioResponse>>> List(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Portfolio> portfolios =
            await _portfolioApplicationService.ListPortfoliosAsync(cancellationToken);

        return Ok(portfolios.Select(ToResponse).ToArray());
    }

    /// <summary>
    /// Retrieves one portfolio.
    /// </summary>
    /// <param name="id">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The portfolio, or a not-found Problem Details response.</returns>
    [HttpGet("{id}")]
    [EndpointSummary("Get a portfolio")]
    [EndpointDescription("Returns one portfolio by identifier.")]
    [ProducesResponseType(typeof(PortfolioResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        Portfolio? portfolio = await _portfolioApplicationService.GetPortfolioAsync(id, cancellationToken);
        if (portfolio is null)
        {
            return Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Portfolio not found",
                detail: "No portfolio exists with the requested identifier.");
        }

        return Ok(ToResponse(portfolio));
    }

    /// <summary>
    /// Retrieves positions derived from the portfolio's complete trade history.
    /// </summary>
    /// <param name="id">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that cancels the query.</param>
    /// <returns>The current positive positions; an empty array when the portfolio has no holdings.</returns>
    [HttpGet("{id}/positions")]
    [EndpointSummary("Get portfolio positions")]
    [EndpointDescription("Calculates positive holdings from the portfolio's complete trade history.")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PositionResponse>>> GetPositions(
        Guid id,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Position> positions =
            await _portfolioApplicationService.GetPositionsAsync(id, cancellationToken);

        return Ok(positions
            .Select(position => new PositionResponse(position.InstrumentId, position.Quantity))
            .ToArray());
    }

    /// <summary>
    /// Values all open positions using trades and market prices eligible at one UTC cutoff.
    /// </summary>
    /// <param name="id">The portfolio identifier.</param>
    /// <param name="asOfUtc">An optional zero-offset UTC cutoff; omitted values use the Application clock.</param>
    /// <param name="cancellationToken">A token that cancels the request and its database reads.</param>
    /// <returns>A complete portfolio valuation, or a Problem Details response when valuation is unavailable.</returns>
    [HttpGet("{id}/valuation")]
    [EndpointSummary("Value a portfolio")]
    [EndpointDescription("Calculates a complete USD market valuation from trades and prices eligible at one UTC cutoff.")]
    [ProducesResponseType(typeof(PortfolioValuationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PortfolioValuationResponse>> GetValuation(
        Guid id,
        [FromQuery] DateTimeOffset? asOfUtc,
        CancellationToken cancellationToken)
    {
        if (asOfUtc.HasValue && !ApiRequestValidation.IsUtc(asOfUtc))
        {
            ModelState.AddModelError(nameof(asOfUtc), "asOfUtc must use the UTC offset.");
            return ValidationProblem(ModelState);
        }

        PortfolioValuation valuation = await _portfolioValuationApplicationService.GetPortfolioValuationAsync(
                id,
                asOfUtc,
                cancellationToken);

        return Ok(new PortfolioValuationResponse(
            valuation.PortfolioId,
            valuation.Currency,
            valuation.AsOfUtc,
            valuation.Positions
                .Select(position => new PortfolioValuationPositionResponse(
                    position.InstrumentId,
                    position.Symbol,
                    position.Quantity,
                    position.MarketPrice,
                    position.PriceAsOfUtc,
                    position.MarketValue))
                .ToArray(),
            valuation.TotalMarketValue));
    }

    /// <summary>
    /// Records a BUY or SELL through the transaction-safe trade use case.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="request">The trade request and stable idempotency key.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The stored trade; an identical retry returns the same trade representation.</returns>
    [HttpPost("{portfolioId}/trades")]
    [EndpointSummary("Record a portfolio trade")]
    [EndpointDescription("Records a BUY or SELL with a caller-provided idempotency key and transaction-safe position validation.")]
    [ProducesResponseType(typeof(TradeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TradeResponse>> RecordTrade(
        Guid portfolioId,
        RecordTradeRequest request,
        CancellationToken cancellationToken)
    {
        Trade trade = await _tradeApplicationService.RecordTradeAsync(
            request.TradeId!.Value,
            portfolioId,
            request.InstrumentId!.Value,
            request.Side!.Value,
            request.Quantity!.Value,
            request.UnitPrice!.Value,
            request.ExecutedAt!.Value,
            cancellationToken);

        return Ok(ToResponse(trade));
    }

    private static PortfolioResponse ToResponse(Portfolio portfolio)
    {
        return new PortfolioResponse(
            portfolio.Id,
            portfolio.Name,
            portfolio.BaseCurrency,
            portfolio.CreatedAt);
    }

    private static TradeResponse ToResponse(Trade trade)
    {
        return new TradeResponse(
            trade.Id,
            trade.PortfolioId,
            trade.InstrumentId,
            trade.Side,
            trade.Quantity,
            trade.UnitPrice,
            trade.ExecutedAt);
    }
}
