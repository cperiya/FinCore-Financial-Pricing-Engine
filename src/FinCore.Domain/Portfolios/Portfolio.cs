namespace FinCore.Domain.Portfolios;

/// <summary>
/// Represents a USD-denominated portfolio and its creation time.
/// </summary>
public sealed class Portfolio
{
    /// <summary>
    /// Gets the maximum supported portfolio name length.
    /// </summary>
    public const int MaximumNameLength = 200;

    /// <summary>
    /// Initializes a portfolio with its identity, name, currency, and UTC creation time.
    /// </summary>
    /// <param name="id">The non-empty portfolio identifier.</param>
    /// <param name="name">The non-empty portfolio name.</param>
    /// <param name="baseCurrency">The portfolio currency; only USD is supported.</param>
    /// <param name="createdAt">The portfolio creation timestamp, which must be UTC.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> is blank or <paramref name="baseCurrency"/> is not USD.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="id"/> is empty or <paramref name="name"/> exceeds
    /// <see cref="MaximumNameLength"/> characters.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="createdAt"/> is not UTC.</exception>
    public Portfolio(Guid id, string name, string baseCurrency, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Portfolio identifier must not be empty.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalizedName = name.Trim();
        if (normalizedName.Length > MaximumNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                $"Portfolio name cannot exceed {MaximumNameLength} characters.");
        }

        Id = id;
        Name = normalizedName;
        BaseCurrency = CurrencyRules.NormalizeUsd(baseCurrency, nameof(baseCurrency));
        CreatedAt = UtcTimestamp.RequireUtc(createdAt, nameof(createdAt));
    }

    /// <summary>
    /// Gets the portfolio identifier.
    /// </summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Gets the portfolio name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// Gets the portfolio's supported base currency, USD.
    /// </summary>
    public string BaseCurrency { get; private set; }

    /// <summary>
    /// Gets the UTC timestamp when the portfolio was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
