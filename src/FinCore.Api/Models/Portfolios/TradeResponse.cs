using FinCore.Domain.Portfolios;

namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// A recorded portfolio trade returned by the API.
/// </summary>
/// <param name="Id">The stable trade identifier and idempotency key.</param>
/// <param name="PortfolioId">The portfolio identifier.</param>
/// <param name="InstrumentId">The instrument identifier.</param>
/// <param name="Side">The trade side, serialized as a string.</param>
/// <param name="Quantity">The traded quantity.</param>
/// <param name="UnitPrice">The USD execution price per unit.</param>
/// <param name="ExecutedAt">The UTC execution time.</param>
public sealed record TradeResponse(
    Guid Id,
    Guid PortfolioId,
    Guid InstrumentId,
    TradeSide Side,
    decimal Quantity,
    decimal UnitPrice,
    DateTimeOffset ExecutedAt);
