using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server-backed simulated market-price repository.
/// </summary>
internal sealed class MarketPriceRepository : IMarketPriceRepository
{
    private const int MaximumIdsPerQuery = 2_000;
    private readonly FinCoreDbContext _dbContext;
    private readonly bool _useJsonParameterForIds;

    /// <summary>
    /// Initializes a repository using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context that tracks market-price operations.</param>
    /// <param name="options">The opt-in ID collection parameter translation settings.</param>
    public MarketPriceRepository(
        FinCoreDbContext dbContext,
        IOptions<PortfolioCollectionParameterOptions> options)
    {
        _dbContext = dbContext;
        _useJsonParameterForIds = options.Value.UseJsonParameterForIds;
    }

    /// <inheritdoc />
    public async Task AddAsync(MarketPrice marketPrice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(marketPrice);
        await _dbContext.MarketPrices.AddAsync(marketPrice, cancellationToken);
    }

    /// <inheritdoc />
    public Task<MarketPrice?> GetLatestByInstrumentIdAsync(
        Guid instrumentId,
        CancellationToken cancellationToken)
    {
        return _dbContext.MarketPrices
            .AsNoTracking()
            .Where(marketPrice => marketPrice.InstrumentId == instrumentId)
            .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
            .ThenByDescending(marketPrice => marketPrice.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MarketPrice?> GetLatestAtOrBeforeAsync(
        Guid instrumentId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        if (asOfUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(asOfUtc));
        }

        return _dbContext.MarketPrices
            .AsNoTracking()
            .Where(marketPrice =>
                marketPrice.InstrumentId == instrumentId &&
                marketPrice.AsOfUtc <= asOfUtc)
            .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
            .ThenByDescending(marketPrice => marketPrice.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketPrice>> GetLatestAtOrBeforeAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        if (asOfUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use the UTC offset.", nameof(asOfUtc));
        }

        Guid[] distinctIds = instrumentIds.Distinct().ToArray();
        List<MarketPrice> latestPrices = new(distinctIds.Length);
        for (int offset = 0; offset < distinctIds.Length; offset += MaximumIdsPerQuery)
        {
            Guid[] batch = distinctIds.Skip(offset).Take(MaximumIdsPerQuery).ToArray();
            IQueryable<MarketPrice> query = _dbContext.MarketPrices.AsNoTracking();
            query = _useJsonParameterForIds
                ? query.Where(marketPrice =>
                    EF.Parameter(batch).Contains(marketPrice.InstrumentId) &&
                    marketPrice.AsOfUtc <= asOfUtc)
                : query.Where(marketPrice =>
                    batch.Contains(marketPrice.InstrumentId) &&
                    marketPrice.AsOfUtc <= asOfUtc);
            MarketPrice[] matches = await query
                .GroupBy(marketPrice => marketPrice.InstrumentId)
                .Select(group => group
                    .OrderByDescending(marketPrice => marketPrice.AsOfUtc)
                    .ThenByDescending(marketPrice => marketPrice.Id)
                    .First())
                .ToArrayAsync(cancellationToken);
            latestPrices.AddRange(matches);
        }

        return latestPrices;
    }
}
