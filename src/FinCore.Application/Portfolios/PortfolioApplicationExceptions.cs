namespace FinCore.Application.Portfolios;

/// <summary>
/// Reports that a requested portfolio does not exist.
/// </summary>
public sealed class PortfolioNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for a missing portfolio.
    /// </summary>
    /// <param name="portfolioId">The portfolio identifier that was not found.</param>
    public PortfolioNotFoundException(Guid portfolioId)
        : base($"Portfolio '{portfolioId}' was not found.")
    {
        PortfolioId = portfolioId;
    }

    /// <summary>
    /// Gets the missing portfolio identifier.
    /// </summary>
    public Guid PortfolioId { get; }
}

/// <summary>
/// Reports that a requested instrument does not exist.
/// </summary>
public sealed class InstrumentNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for a missing instrument.
    /// </summary>
    /// <param name="instrumentId">The instrument identifier that was not found.</param>
    public InstrumentNotFoundException(Guid instrumentId)
        : base($"Instrument '{instrumentId}' was not found.")
    {
        InstrumentId = instrumentId;
    }

    /// <summary>
    /// Gets the missing instrument identifier.
    /// </summary>
    public Guid InstrumentId { get; }
}

/// <summary>
/// Reports an attempt to register a symbol that is already in use.
/// </summary>
public sealed class InstrumentSymbolAlreadyRegisteredException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for a symbol that is already registered.
    /// </summary>
    /// <param name="symbol">The normalized instrument symbol.</param>
    public InstrumentSymbolAlreadyRegisteredException(string symbol)
        : base($"Instrument symbol '{symbol}' is already registered.")
    {
        Symbol = symbol;
    }

    /// <summary>
    /// Gets the normalized instrument symbol that conflicts with an existing instrument.
    /// </summary>
    public string Symbol { get; }
}

/// <summary>
/// Reports reuse of a trade identifier with a different canonical request payload.
/// </summary>
public sealed class TradeIdempotencyConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for a conflicting request using an existing trade identifier.
    /// </summary>
    /// <param name="tradeId">The trade identifier whose stored payload differs.</param>
    public TradeIdempotencyConflictException(Guid tradeId)
        : base($"Trade identifier '{tradeId}' was already used with a different request.")
    {
        TradeId = tradeId;
    }

    /// <summary>
    /// Gets the trade identifier whose payload conflicts with the request.
    /// </summary>
    public Guid TradeId { get; }
}

/// <summary>
/// Identifies an instrument that has no market price eligible for a valuation cutoff.
/// </summary>
/// <param name="InstrumentId">The missing-price instrument identifier.</param>
/// <param name="Symbol">The normalized instrument symbol.</param>
public sealed record MissingMarketPriceInstrument(Guid InstrumentId, string Symbol);

/// <summary>
/// Reports that one or more open positions cannot be valued because no eligible prices exist.
/// </summary>
public sealed class MissingMarketPricesException : InvalidOperationException
{
    /// <summary>
    /// Initializes a missing-price failure for a portfolio valuation.
    /// </summary>
    /// <param name="portfolioId">The portfolio that could not be completely valued.</param>
    /// <param name="asOfUtc">The valuation cutoff.</param>
    /// <param name="missingInstruments">All open-position instruments without eligible prices.</param>
    public MissingMarketPricesException(
        Guid portfolioId,
        DateTimeOffset asOfUtc,
        IReadOnlyList<MissingMarketPriceInstrument> missingInstruments)
        : base($"Portfolio '{portfolioId}' has positions without market prices eligible at '{asOfUtc:O}'.")
    {
        ArgumentNullException.ThrowIfNull(missingInstruments);
        PortfolioId = portfolioId;
        AsOfUtc = asOfUtc;
        MissingInstruments = missingInstruments;
    }

    /// <summary>
    /// Gets the portfolio that could not be completely valued.
    /// </summary>
    public Guid PortfolioId { get; }

    /// <summary>
    /// Gets the UTC cutoff used for the attempted valuation.
    /// </summary>
    public DateTimeOffset AsOfUtc { get; }

    /// <summary>
    /// Gets all open-position instruments without an eligible market price.
    /// </summary>
    public IReadOnlyList<MissingMarketPriceInstrument> MissingInstruments { get; }
}

/// <summary>
/// Reports that a checked position value or portfolio total exceeded the decimal range.
/// </summary>
public sealed class PortfolioValuationOverflowException : ArithmeticException
{
    /// <summary>
    /// Initializes a valuation overflow failure.
    /// </summary>
    /// <param name="portfolioId">The portfolio being valued.</param>
    /// <param name="asOfUtc">The cutoff used for the valuation.</param>
    /// <param name="innerException">The decimal overflow raised during valuation.</param>
    public PortfolioValuationOverflowException(
        Guid portfolioId,
        DateTimeOffset asOfUtc,
        OverflowException innerException)
        : base($"The valuation for portfolio '{portfolioId}' exceeds the supported decimal range.", innerException)
    {
        PortfolioId = portfolioId;
        AsOfUtc = asOfUtc;
    }

    /// <summary>
    /// Gets the portfolio being valued.
    /// </summary>
    public Guid PortfolioId { get; }

    /// <summary>
    /// Gets the UTC cutoff used for the valuation.
    /// </summary>
    public DateTimeOffset AsOfUtc { get; }
}
