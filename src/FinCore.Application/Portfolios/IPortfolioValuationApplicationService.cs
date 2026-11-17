namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines the use case for valuing a portfolio at a UTC cutoff.
/// </summary>
public interface IPortfolioValuationApplicationService
{
    /// <summary>
    /// Calculates a portfolio's position-level and total market value at the requested cutoff.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="asOfUtc">
    /// The inclusive UTC cutoff for trades and prices; when absent, the current UTC time is captured once.
    /// </param>
    /// <param name="cancellationToken">A token that can cancel the transaction and its queries.</param>
    /// <returns>The complete USD valuation, with zero total and no positions when there are no open holdings.</returns>
    /// <exception cref="PortfolioNotFoundException">Thrown when the portfolio does not exist.</exception>
    /// <exception cref="MissingMarketPricesException">
    /// Thrown when any open position lacks an eligible market price.
    /// </exception>
    /// <exception cref="PortfolioValuationOverflowException">
    /// Thrown when a position value or portfolio total exceeds the range of <see cref="decimal"/>.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when the supplied cutoff is not UTC.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the request is canceled.</exception>
    /// <exception cref="FinCore.Application.Persistence.TransactionDeadlockException">
    /// Thrown when SQL Server selects the valuation transaction as a deadlock victim.
    /// </exception>
    Task<PortfolioValuation> GetPortfolioValuationAsync(
        Guid portfolioId,
        DateTimeOffset? asOfUtc,
        CancellationToken cancellationToken);
}
