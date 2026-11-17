namespace FinCore.Api.Models.Portfolios;

/// <summary>
/// A positive instrument holding derived from a portfolio's trades.
/// </summary>
/// <param name="InstrumentId">The held instrument identifier.</param>
/// <param name="Quantity">The net quantity currently held.</param>
public sealed record PositionResponse(Guid InstrumentId, decimal Quantity);
