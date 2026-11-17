using FinCore.Domain.Portfolios;

namespace FinCore.Application.Portfolios;

/// <summary>
/// Defines use cases for registering supported equity instruments.
/// </summary>
public interface IInstrumentApplicationService
{
    /// <summary>
    /// Registers a USD equity using its normalized, unique symbol.
    /// </summary>
    /// <param name="symbol">The equity symbol to normalize and register.</param>
    /// <param name="name">The instrument name.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The registered instrument.</returns>
    /// <exception cref="InstrumentSymbolAlreadyRegisteredException">
    /// Thrown when the normalized symbol is already registered.
    /// </exception>
    Task<Instrument> RegisterEquityAsync(
        string symbol,
        string name,
        CancellationToken cancellationToken);
}
