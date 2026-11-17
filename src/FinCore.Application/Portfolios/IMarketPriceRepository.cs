using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines persistence operations for simulated instrument market prices.
/// </summary>
public interface IMarketPriceRepository
{
    /// <summary>
    /// Adds a market price to the current unit of work.
    /// </summary>
    /// <param name="marketPrice">The validated market price to add.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>A task that represents the asynchronous add operation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="marketPrice"/> is null.</exception>
    Task AddAsync(MarketPrice marketPrice, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the latest market price recorded for an instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>
    /// The price with the greatest observation timestamp, breaking ties by descending price identifier,
    /// or <see langword="null"/> when no price exists.
    /// </returns>
    Task<MarketPrice?> GetLatestByInstrumentIdAsync(
        Guid instrumentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the latest price whose observation time is not later than the requested UTC time.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="asOfUtc">The UTC cutoff time.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>
    /// The latest eligible price, breaking equal-time ties by descending price identifier,
    /// or <see langword="null"/> when no eligible price exists.
    /// </returns>
    Task<MarketPrice?> GetLatestAtOrBeforeAsync(
        Guid instrumentId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the latest eligible price for each requested instrument using bounded batch queries.
    /// </summary>
    /// <param name="instrumentIds">The instrument identifiers to price.</param>
    /// <param name="asOfUtc">The inclusive UTC observation-time cutoff.</param>
    /// <param name="cancellationToken">A token that can cancel the queries.</param>
    /// <returns>
    /// At most one price per instrument, chosen by descending observation time and then descending price ID.
    /// Instruments without an eligible price are omitted.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instrumentIds"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="asOfUtc"/> is not UTC.</exception>
    Task<IReadOnlyList<MarketPrice>> GetLatestAtOrBeforeAsync(
        IReadOnlyCollection<Guid> instrumentIds,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken);
}
