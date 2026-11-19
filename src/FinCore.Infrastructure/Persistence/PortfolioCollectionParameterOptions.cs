namespace FinCore.Infrastructure.Persistence;

/// <summary>
/// Controls the opt-in EF Core translation used for portfolio valuation ID collections.
/// </summary>
public sealed class PortfolioCollectionParameterOptions
{
    /// <summary>
    /// Gets or sets whether ID collection predicates use one JSON parameter instead of scalar parameters.
    /// </summary>
    public bool UseJsonParameterForIds { get; set; }
}
