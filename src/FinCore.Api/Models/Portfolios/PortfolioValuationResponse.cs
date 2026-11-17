namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// A complete USD market valuation of a portfolio at one UTC cutoff.
/// </summary>
/// <param name="PortfolioId">The portfolio identifier.</param>
/// <param name="Currency">The portfolio base currency.</param>
/// <param name="AsOfUtc">The effective UTC cutoff used for trade and price selection.</param>
/// <param name="Positions">The valued positive holdings.</param>
/// <param name="TotalMarketValue">The sum of position market values without rounding.</param>
public sealed record PortfolioValuationResponse(
    Guid PortfolioId,
    string Currency,
    DateTimeOffset AsOfUtc,
    IReadOnlyList<PortfolioValuationPositionResponse> Positions,
    decimal TotalMarketValue);
