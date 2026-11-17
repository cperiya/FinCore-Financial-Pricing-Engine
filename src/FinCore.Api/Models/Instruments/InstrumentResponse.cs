using FinCore.Domain.Portfolios;

namespace FinCore.Api.Models.Instruments;

/// <summary>
/// A registered instrument returned by the API.
/// </summary>
/// <param name="Id">The instrument identifier.</param>
/// <param name="Symbol">The normalized uppercase symbol.</param>
/// <param name="Name">The instrument name.</param>
/// <param name="Currency">The instrument currency.</param>
/// <param name="InstrumentType">The instrument type, serialized as a string.</param>
public sealed record InstrumentResponse(
    Guid Id,
    string Symbol,
    string Name,
    string Currency,
    InstrumentType InstrumentType);
