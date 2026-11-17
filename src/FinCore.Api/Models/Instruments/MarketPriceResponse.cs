namespace FinCore.Api.Models.Instruments;

/// <summary>
/// A simulated market price returned by the API.
/// </summary>
/// <param name="Id">The market-price identifier.</param>
/// <param name="InstrumentId">The priced instrument identifier.</param>
/// <param name="Price">The positive USD price.</param>
/// <param name="Currency">The price currency.</param>
/// <param name="AsOfUtc">The UTC time at which the price applies.</param>
public sealed record MarketPriceResponse(
    Guid Id,
    Guid InstrumentId,
    decimal Price,
    string Currency,
    DateTimeOffset AsOfUtc);
