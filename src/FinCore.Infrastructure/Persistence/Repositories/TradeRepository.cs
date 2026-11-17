using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server-backed trade repository.
/// </summary>
internal sealed class TradeRepository : ITradeRepository
{
    private readonly FinCoreDbContext _dbContext;

    /// <summary>
    /// Initializes a repository using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context that tracks trade operations.</param>
    public TradeRepository(FinCoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(Trade trade, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trade);
        await _dbContext.Trades.AddAsync(trade, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Trade?> GetByIdAsync(Guid tradeId, CancellationToken cancellationToken)
    {
        return _dbContext.Trades
            .AsNoTracking()
            .SingleOrDefaultAsync(trade => trade.Id == tradeId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Trade>> GetByPortfolioIdAsync(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        return await OrderForPositionCalculation(_dbContext.Trades
                .AsNoTracking()
                .Where(trade => trade.PortfolioId == portfolioId))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Trade>> GetByPortfolioIdAtOrBeforeAsync(
        Guid portfolioId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (asOfUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(asOfUtc));
        }

        return await OrderForPositionCalculation(_dbContext.Trades
                .AsNoTracking()
                .Where(trade => trade.PortfolioId == portfolioId && trade.ExecutedAt <= asOfUtc))
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Trade>> GetByPortfolioAndInstrumentAsync(
        Guid portfolioId,
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        return await OrderForPositionCalculation(_dbContext.Trades
                .AsNoTracking()
                .Where(trade =>
                    trade.PortfolioId == portfolioId &&
                    trade.InstrumentId == instrumentId))
            .ToArrayAsync(cancellationToken);
    }

    private static IOrderedQueryable<Trade> OrderForPositionCalculation(IQueryable<Trade> trades)
    {
        return trades
            .OrderBy(trade => trade.ExecutedAt)
            .ThenBy(trade => trade.Side)
            .ThenBy(trade => trade.Id);
    }
}
