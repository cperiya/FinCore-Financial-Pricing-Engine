using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.EntityFrameworkCore;

namespace FinCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server-backed instrument repository.
/// </summary>
internal sealed class InstrumentRepository : IInstrumentRepository
{
    private const int MaximumIdsPerQuery = 2_000;
    private readonly FinCoreDbContext _dbContext;

    /// <summary>
    /// Initializes a repository using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context that tracks instrument operations.</param>
    public InstrumentRepository(FinCoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task AddAsync(Instrument instrument, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        await _dbContext.Instruments.AddAsync(instrument, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Instrument?> GetByIdAsync(Guid instrumentId, CancellationToken cancellationToken)
    {
        return _dbContext.Instruments
            .AsNoTracking()
            .SingleOrDefaultAsync(instrument => instrument.Id == instrumentId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Instrument>> GetByIdsAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        Guid[] distinctIds = instrumentIds.Distinct().ToArray();
        List<Instrument> instruments = new(distinctIds.Length);

        for (int offset = 0; offset < distinctIds.Length; offset += MaximumIdsPerQuery)
        {
            Guid[] batch = distinctIds.Skip(offset).Take(MaximumIdsPerQuery).ToArray();
            Instrument[] matches = await _dbContext.Instruments
                .AsNoTracking()
                .Where(instrument => batch.Contains(instrument.Id))
                .ToArrayAsync(cancellationToken);
            instruments.AddRange(matches);
        }

        return instruments;
    }

    /// <inheritdoc />
    public Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken)
    {
        string normalizedSymbol = Instrument.NormalizeSymbol(symbol);
        return _dbContext.Instruments
            .AsNoTracking()
            .SingleOrDefaultAsync(instrument => instrument.Symbol == normalizedSymbol, cancellationToken);
    }
}
