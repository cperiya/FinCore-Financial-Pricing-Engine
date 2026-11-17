using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines idempotent, transaction-safe portfolio trade use cases.
/// </summary>
public interface ITradeApplicationService
{
    /// <summary>
    /// Records a BUY or SELL after validating the complete chronological position history.
    /// </summary>
    /// <param name="tradeId">The caller-supplied stable trade identifier and idempotency key.</param>
    /// <param name="portfolioId">The portfolio identifier.</param>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="side">Whether the trade buys or sells the instrument.</param>
    /// <param name="quantity">The positive decimal quantity traded.</param>
    /// <param name="unitPrice">The positive USD execution price per unit.</param>
    /// <param name="executedAt">The UTC execution timestamp.</param>
    /// <param name="cancellationToken">A token that can cancel locking, queries, saving, or commit.</param>
    /// <returns>The stored trade, including the original trade for an identical retry.</returns>
    /// <exception cref="PortfolioNotFoundException">Thrown when the portfolio does not exist.</exception>
    /// <exception cref="InstrumentNotFoundException">Thrown when the instrument does not exist.</exception>
    /// <exception cref="InsufficientPositionException">
    /// Thrown when the complete chronological history would make a position negative.
    /// </exception>
    /// <exception cref="TradeIdempotencyConflictException">
    /// Thrown when <paramref name="tradeId"/> already identifies a different canonical request.
    /// </exception>
    /// <exception cref="TimeoutException">Thrown when a required SQL Server application lock times out.</exception>
    /// <exception cref="TransactionDeadlockException">
    /// Thrown when SQL Server selects the transaction as a deadlock victim.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<Trade> RecordTradeAsync(
        Guid tradeId,
        Guid portfolioId,
        Guid instrumentId,
        TradeSide side,
        decimal quantity,
        decimal unitPrice,
        DateTimeOffset executedAt,
        CancellationToken cancellationToken);
}
