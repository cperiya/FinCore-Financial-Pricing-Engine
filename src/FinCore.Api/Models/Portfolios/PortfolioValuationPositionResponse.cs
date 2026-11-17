namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// Market-price and value details for one positive portfolio holding.
/// </summary>
/// <param name="InstrumentId">The instrument identifier.</param>
/// <param name="Symbol">The normalized instrument symbol.</param>
/// <param name="Quantity">The positive quantity held at the valuation cutoff.</param>
/// <param name="MarketPrice">The selected USD price per share.</param>
/// <param name="PriceAsOfUtc">The UTC observation time of the selected market price.</param>
/// <param name="MarketValue">The unrounded USD market value of the holding.</param>
public sealed record PortfolioValuationPositionResponse(
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal MarketPrice,
    DateTimeOffset PriceAsOfUtc,
    decimal MarketValue);
