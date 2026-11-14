using FinCore.Api.Models;
using FinCore.Application.Pricing;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

/// <summary>
/// Exposes pricing operations over HTTP.
/// </summary>
[ApiController]
[Route("api/pricing")]
public sealed class PricingController : ControllerBase
{
    private readonly IStockPricingService _stockPricingService;

    /// <summary>
    /// Initializes the controller with the stock pricing use case.
    /// </summary>
    /// <param name="stockPricingService">The application service used to value stock positions.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="stockPricingService"/> is <see langword="null"/>.
    /// </exception>
    public PricingController(IStockPricingService stockPricingService)
    {
        ArgumentNullException.ThrowIfNull(stockPricingService);
        _stockPricingService = stockPricingService;
    }

    /// <summary>
    /// Calculates the market value of a stock position.
    /// </summary>
    /// <param name="request">The symbol, unit price, and quantity supplied as query parameters.</param>
    /// <returns>The position inputs and calculated market value, or a validation problem for invalid inputs.</returns>
    /// <response code="200">The position was valued successfully.</response>
    /// <response code="400">The symbol is missing or blank, a numeric input is missing or not positive, an input is malformed, or the result exceeds decimal range.</response>
    [HttpGet("stock")]
    [ProducesResponseType(typeof(StockValuationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public ActionResult<StockValuationResponse> GetStock([FromQuery] StockValuationRequest request)
    {
        try
        {
            StockValuationResult result = _stockPricingService.ValueStock(
                request.Symbol!,
                request.UnitPrice!.Value,
                request.Quantity!.Value);

            return Ok(new StockValuationResponse(
                result.Symbol,
                result.UnitPrice,
                result.Quantity,
                result.MarketValue));
        }
        catch (OverflowException)
        {
            ModelState.AddModelError(
                "marketValue",
                "The calculated market value exceeds the supported decimal range.");

            return ValidationProblem(ModelState);
        }
    }
}
