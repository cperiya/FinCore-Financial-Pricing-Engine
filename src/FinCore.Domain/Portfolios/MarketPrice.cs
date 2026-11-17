namespace FinCore.Domain.Portfolios;

/// <summary>
/// Represents a simulated USD market price for an instrument at a UTC time.
/// </summary>
public sealed class MarketPrice
{
    /// <summary>
    /// Initializes a market price with a positive USD amount and UTC observation time.
    /// </summary>
    /// <param name="id">The non-empty market-price identifier.</param>
    /// <param name="instrumentId">The non-empty identifier of the priced instrument.</param>
    /// <param name="price">The positive price in USD.</param>
    /// <param name="currency">The price currency; only USD is supported.</param>
    /// <param name="asOfUtc">The UTC time at which the price applies.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an identifier is empty or <paramref name="price"/> is not positive.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="currency"/> is not USD or <paramref name="asOfUtc"/> is not UTC.
    /// </exception>
    public MarketPrice(
        Guid id,
        Guid instrumentId,
        decimal price,
        string currency,
        DateTimeOffset asOfUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(id),
                id,
                "Market-price identifier must not be empty.");
        }

        if (instrumentId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentId),
                instrumentId,
                "Instrument identifier must not be empty.");
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                price,
                "Market price must be greater than zero.");
        }

        Id = id;
        InstrumentId = instrumentId;
        Price = price;
        Currency = CurrencyRules.NormalizeUsd(currency, nameof(currency));
        AsOfUtc = UtcTimestamp.RequireUtc(asOfUtc, nameof(asOfUtc));
    }

    /// <summary>
    /// Gets the market-price identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the identifier of the priced instrument.
    /// </summary>
    public Guid InstrumentId { get; private set; }

    /// <summary>
    /// Gets the positive price in USD.
    /// </summary>
    public decimal Price { get; private set; }

    /// <summary>
    /// Gets the supported price currency, USD.
    /// </summary>
    public string Currency { get; private set; }

    /// <summary>
    /// Gets the UTC time at which the price applies.
    /// </summary>
    public DateTimeOffset AsOfUtc { get; private set; }
}
