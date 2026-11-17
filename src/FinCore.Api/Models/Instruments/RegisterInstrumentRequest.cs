using System.ComponentModel.DataAnnotations;
using FinCore.Domain.Portfolios;

namespace FinCore.Api.Models.Instruments;

/// <summary>
/// Request to register a USD equity instrument.
/// </summary>
public sealed record RegisterInstrumentRequest : IValidatableObject
{
    /// <summary>
    /// Gets the equity symbol to normalize and register.
    /// </summary>
    [Required]
    public string? Symbol { get; init; }

    /// <summary>
    /// Gets the instrument name.
    /// </summary>
    [Required]
    public string? Name { get; init; }

    /// <summary>
    /// Gets the requested instrument currency; only USD is supported.
    /// </summary>
    [Required]
    public string? Currency { get; init; }

    /// <summary>
    /// Validates symbol, name, and supported currency.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Validation errors for unsupported values.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Symbol is not null && string.IsNullOrWhiteSpace(Symbol))
        {
            yield return new ValidationResult("A non-blank symbol is required.", [nameof(Symbol)]);
        }
        else if (Symbol is not null)
        {
            bool isValidSymbol;
            try
            {
                Instrument.NormalizeSymbol(Symbol);
                isValidSymbol = true;
            }
            catch (ArgumentException)
            {
                isValidSymbol = false;
            }

            if (!isValidSymbol)
            {
                yield return new ValidationResult(
                    $"Symbol must be at most {Instrument.MaximumSymbolLength} characters after trimming.",
                    [nameof(Symbol)]);
            }
        }

        if (Name is not null && !ApiRequestValidation.IsText(Name, Instrument.MaximumNameLength))
        {
            yield return new ValidationResult(
                $"A non-blank name of at most {Instrument.MaximumNameLength} characters is required.",
                [nameof(Name)]);
        }

        if (Currency is not null && !ApiRequestValidation.IsUsd(Currency))
        {
            yield return new ValidationResult("Only USD instruments are supported.", [nameof(Currency)]);
        }
    }
}
