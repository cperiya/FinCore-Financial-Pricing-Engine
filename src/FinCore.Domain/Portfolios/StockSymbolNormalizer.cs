namespace FinCore.Domain.Portfolios;

/// <summary>
/// Normalizes stock symbols without imposing the storage limit for registered instruments.
/// </summary>
public static class StockSymbolNormalizer
{
    /// <summary>
    /// Trims and uppercases a non-blank symbol using invariant casing.
    /// </summary>
    /// <param name="symbol">The non-blank symbol to normalize.</param>
    /// <returns>The trimmed symbol in uppercase.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="symbol"/> is blank.</exception>
    public static string Normalize(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        return symbol.Trim().ToUpperInvariant();
    }
}
