using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines persistence operations for supported instrument registration and retrieval.
/// </summary>
public interface IInstrumentRepository
{
    /// <summary>
    /// Adds an instrument to the current unit of work.
    /// </summary>
    /// <param name="instrument">The validated instrument to add.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instrument"/> is null.</exception>
    Task AddAsync(Instrument instrument, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an instrument by its identifier.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when it does not exist.</returns>
    Task<Instrument?> GetByIdAsync(Guid instrumentId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves instruments matching a collection of identifiers using bounded batch queries.
    /// </summary>
    /// <param name="instrumentIds">The instrument identifiers to retrieve.</param>
    /// <param name="cancellationToken">A token that can cancel the queries.</param>
    /// <returns>The matching instruments; missing identifiers are not included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instrumentIds"/> is null.</exception>
    Task<IReadOnlyList<Instrument>> GetByIdsAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves an instrument by its normalized symbol.
    /// </summary>
    /// <param name="symbol">The symbol to normalize and look up.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The matching instrument, or <see langword="null"/> when it does not exist.</returns>
    Task<Instrument?> GetBySymbolAsync(string symbol, CancellationToken cancellationToken);
}
