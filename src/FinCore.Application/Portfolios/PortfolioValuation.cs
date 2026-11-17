namespace FinCore.Application.Portfolios;

/// <summary>
/// Contains a complete portfolio valuation calculated at one UTC cutoff.
/// </summary>
/// <param name="PortfolioId">The valued portfolio identifier.</param>
/// <param name="Currency">The portfolio base currency, currently USD.</param>
/// <param name="AsOfUtc">The single effective UTC cutoff used for trade and price selection.</param>
/// <param name="Positions">The valued positive holdings, in instrument-ID order.</param>
/// <param name="TotalMarketValue">The sum of position market values without rounding.</param>
public sealed record PortfolioValuation(
    Guid PortfolioId,
    string Currency,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<PortfolioValuationPosition> Positions,
    decimal TotalMarketValue);

/// <summary>
/// Contains the selected market price and calculated value for one open holding.
/// </summary>
/// <param name="InstrumentId">The valued instrument identifier.</param>
/// <param name="Symbol">The normalized instrument symbol.</param>
/// <param name="Quantity">The positive quantity held at the valuation cutoff.</param>
/// <param name="MarketPrice">The selected positive USD market price.</param>
/// <param name="PriceAsOfUtc">The UTC observation time of the selected price.</param>
/// <param name="MarketValue">The unrounded USD market value of the holding.</param>
public sealed record PortfolioValuationPosition(
    Guid InstrumentId,
    string Symbol,
    decimal Quantity,
    decimal MarketPrice,
    DateTimeOffset PriceAsOfUtc,
    decimal MarketValue);
