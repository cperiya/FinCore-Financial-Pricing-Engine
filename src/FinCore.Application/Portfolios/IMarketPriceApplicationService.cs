using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines simulated market-price recording and as-of retrieval use cases.
/// </summary>
public interface IMarketPriceApplicationService
{
    /// <summary>
    /// Records a positive simulated USD price for an existing instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="price">The positive price in USD.</param>
    /// <param name="asOfUtc">The UTC timestamp at which the price applies.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The stored market price.</returns>
    /// <exception cref="InstrumentNotFoundException">Thrown when the instrument does not exist.</exception>
    Task<MarketPrice> RecordPriceAsync(
        Guid instrumentId,
        decimal price,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the latest price whose observation time is at or before a UTC cutoff.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier.</param>
    /// <param name="asOfUtc">The UTC cutoff time.</param>
    /// <param name="cancellationToken">A token that can cancel the query.</param>
    /// <returns>The latest eligible price, or <see langword="null"/> when none is available.</returns>
    /// <exception cref="InstrumentNotFoundException">Thrown when the instrument does not exist.</exception>
    Task<MarketPrice?> GetLatestEligiblePriceAsync(
        Guid instrumentId,
        DateTimeOffset asOfUtc,
        CancellationToken cancellationToken);
}
