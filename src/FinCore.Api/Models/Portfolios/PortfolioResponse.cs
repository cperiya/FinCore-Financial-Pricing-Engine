namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// Portfolio details returned by the API.
/// </summary>
/// <param name="Id">The portfolio identifier.</param>
/// <param name="Name">The normalized portfolio name.</param>
/// <param name="BaseCurrency">The portfolio base currency.</param>
/// <param name="CreatedAt">The UTC portfolio creation time.</param>
public sealed record PortfolioResponse(
    Guid Id,
    string Name,
    string BaseCurrency,
    DateTimeOffset CreatedAt);
