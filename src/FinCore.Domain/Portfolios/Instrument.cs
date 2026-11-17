namespace FinCore.Domain.Portfolios;

/// <summary>
/// Represents a supported USD-denominated equity instrument.
/// </summary>
public sealed class Instrument
{
    /// <summary>
    /// Gets the maximum supported instrument symbol length.
    /// </summary>
    public const int MaximumSymbolLength = 20;

    /// <summary>
    /// Gets the maximum supported instrument name length.
    /// </summary>
    public const int MaximumNameLength = 200;

    /// <summary>
    /// Initializes an equity instrument with a normalized symbol and USD currency.
    /// </summary>
    /// <param name="id">The non-empty instrument identifier.</param>
    /// <param name="symbol">The instrument symbol; it is trimmed, uppercased invariantly, and limited to 20 characters.</param>
    /// <param name="name">The non-empty instrument name, limited to 200 characters.</param>
    /// <param name="currency">The instrument currency; only USD is supported.</param>
    /// <param name="instrumentType">The instrument type; only <see cref="InstrumentType.Equity"/> is supported.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="symbol"/> or <paramref name="name"/> is blank, or
    /// <paramref name="currency"/> is not USD.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="id"/> is empty, the symbol or name exceeds its supported length,
    /// or <paramref name="instrumentType"/> is unsupported.
    /// </exception>
    public Instrument(
        Guid id,
        string symbol,
        string name,
        string currency,
        InstrumentType instrumentType)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Instrument identifier must not be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                $"Instrument name cannot exceed {MaximumNameLength} characters.");
        }

        if (instrumentType != InstrumentType.Equity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(instrumentType),
                instrumentType,
                "Only equity instruments are supported.");
        }

        Id = id;
        Symbol = NormalizeSymbol(symbol);
        Name = normalizedName;
        Currency = CurrencyRules.NormalizeUsd(currency, nameof(currency));
        InstrumentType = instrumentType;
    }

    /// <summary>
    /// Gets the instrument identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the trimmed, invariant-uppercase symbol of at most 20 characters.
    /// </summary>
    public string Symbol { get; private set; }

    /// <summary>
    /// Gets the instrument name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the supported instrument currency, USD.
    /// </summary>
    public string Currency { get; private set; }

    /// <summary>
    /// Gets the instrument type.
    /// </summary>
    public InstrumentType InstrumentType { get; private set; }

    /// <summary>
    /// Trims and uppercases a symbol using invariant casing and enforces the supported length.
    /// </summary>
    /// <param name="symbol">The non-blank symbol to normalize; at most 20 characters after normalization.</param>
    /// <returns>The trimmed symbol in uppercase.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="symbol"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the normalized <paramref name="symbol"/> exceeds <see cref="MaximumSymbolLength"/> characters.
    /// </exception>
    public static string NormalizeSymbol(string symbol)
    {
        string normalizedSymbol = StockSymbolNormalizer.Normalize(symbol);
        if (normalizedSymbol.Length > MaximumSymbolLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(symbol),
                symbol,
                $"Instrument symbol cannot exceed {MaximumSymbolLength} characters.");
        }

        return normalizedSymbol;
    }
}
