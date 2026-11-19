using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using FinCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinCore.Infrastructure.Persistence.Repositories;

/// <summary>
/// SQL Server-backed instrument repository.
/// </summary>
internal sealed class InstrumentRepository : IInstrumentRepository
{
    private const int MaximumIdsPerQuery = 2_000;
    private readonly FinCoreDbContext _dbContext;
    private readonly bool _useJsonParameterForIds;

    /// <summary>
    /// Initializes a repository using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context that tracks instrument operations.</param>
    /// <param name="options">The opt-in ID collection parameter translation settings.</param>
    public InstrumentRepository(
        FinCoreDbContext dbContext,
        IOptions<PortfolioCollectionParameterOptions> options)
    {
        _dbContext = dbContext;
        _useJsonParameterForIds = options.Value.UseJsonParameterForIds;
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
            IQueryable<Instrument> query = _dbContext.Instruments.AsNoTracking();
            query = _useJsonParameterForIds
                ? query.Where(instrument => EF.Parameter(batch).Contains(instrument.Id))
                : query.Where(instrument => batch.Contains(instrument.Id));
            Instrument[] matches = await query.ToArrayAsync(cancellationToken);
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
