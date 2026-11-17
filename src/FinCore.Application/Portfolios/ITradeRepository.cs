using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines persistence operations for trades used to derive portfolio positions.
/// </summary>
public interface ITradeRepository
{
    /// <summary>
    /// Adds a trade to the current unit of work without committing it.
    /// </summary>
    /// <param name="trade">The validated trade to add.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trade"/> is null.</exception>
    Task AddAsync(Trade trade, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a trade by its stable identifier used as the idempotency key.
    /// </summary>
    /// <param name="tradeId">The non-empty trade identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The trade, or <see langword="null"/> when it does not exist.</returns>
    Task<Trade?> GetByIdAsync(Guid tradeId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves all trades for a portfolio in deterministic position-calculation order.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The portfolio's trades ordered by execution time, side, and trade identifier.</returns>
    Task<IReadOnlyList<Trade>> GetByPortfolioIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves portfolio trades executed no later than a UTC cutoff in deterministic position-calculation order.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="asOfUtc">The inclusive UTC execution-time cutoff.</param>
    /// <param name="cancellationToken">A token that can cancel the query.</param>
    /// <returns>Matching trades ordered by execution time, side, and trade identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="asOfUtc"/> is not UTC.</exception>
    Task<IReadOnlyList<Trade>> GetByPortfolioIdAtOrBeforeAsync(
        Guid portfolioId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves trades for one portfolio and instrument in deterministic position-calculation order.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The matching trades ordered by execution time, side, and trade identifier.</returns>
    Task<IReadOnlyList<Trade>> GetByPortfolioAndInstrumentAsync(
        Guid portfolioId,
        Guid instrumentId,
        CancellationToken cancellationToken);
}
