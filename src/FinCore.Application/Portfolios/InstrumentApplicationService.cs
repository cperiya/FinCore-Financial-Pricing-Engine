using FinCore.Application.Persistence;
using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Implements normalized, unique USD equity registration.
/// </summary>
public sealed class InstrumentApplicationService : IInstrumentApplicationService
{
    private readonly IInstrumentRepository _instruments;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// Initializes instrument registration with persistence and transaction dependencies.
    /// </summary>
    /// <param name="instruments">Instrument persistence operations.</param>
    /// <param name="unitOfWork">The transaction boundary used to serialize symbol registration.</param>
    public InstrumentApplicationService(IInstrumentRepository instruments, IUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(instruments);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _instruments = instruments;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<Instrument> RegisterEquityAsync(
        string symbol,
        string name,
        CancellationToken cancellationToken)
    {
        string normalizedSymbol = Instrument.NormalizeSymbol(symbol);
        return _unitOfWork.ExecuteWithExclusiveLocksAsync(
            [$"FinCore:InstrumentSymbol:{normalizedSymbol}"],
            async transactionToken =>
            {
                if (await _instruments.GetBySymbolAsync(normalizedSymbol, transactionToken) is not null)
                {
                    throw new InstrumentSymbolAlreadyRegisteredException(normalizedSymbol);
                }

                Instrument instrument = new(
                    Guid.NewGuid(),
                    normalizedSymbol,
                    name,
                    "USD",
                    InstrumentType.Equity);
                await _instruments.AddAsync(instrument, transactionToken);
                return instrument;
            },
            cancellationToken);
    }
}
