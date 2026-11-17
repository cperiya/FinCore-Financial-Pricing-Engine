using FinCore.Application.Persistence;
using FinCore.Application.Portfolios;
using FinCore.Domain.Portfolios;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Middleware;

internal sealed class ApiExceptionHandler(
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>
    /// Converts known request and transaction failures to safe Problem Details responses.
    /// </summary>
    /// <param name="httpContext">The current HTTP request and response.</param>
    /// <param name="exception">The exception raised while handling the request.</param>
    /// <param name="cancellationToken">A token that cancels writing the response.</param>
    /// <returns><see langword="true"/> when the exception was handled.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        (int status, string title, string detail) = exception switch
        {
            PortfolioNotFoundException =>
                (StatusCodes.Status404NotFound, "Portfolio not found", "The requested portfolio does not exist."),
            InstrumentNotFoundException =>
                (StatusCodes.Status404NotFound, "Instrument not found", "The requested instrument does not exist."),
            InstrumentSymbolAlreadyRegisteredException =>
                (StatusCodes.Status409Conflict, "Instrument symbol conflict", "The symbol is already registered."),
            InsufficientPositionException =>
                (StatusCodes.Status409Conflict, "Insufficient position", "The trade would create a negative holding."),
            TradeIdempotencyConflictException =>
                (StatusCodes.Status409Conflict, "Trade idempotency conflict", "The trade identifier was used with a different request."),
            MissingMarketPricesException =>
                (StatusCodes.Status409Conflict, "Market prices unavailable", "One or more open positions have no eligible market price; no partial valuation was returned."),
            PortfolioValuationOverflowException =>
                (StatusCodes.Status422UnprocessableEntity, "Portfolio valuation overflow", "The calculated valuation exceeds the supported decimal range."),
            TimeoutException or TransactionLockAcquisitionException or TransactionDeadlockException or
                TransientPersistenceException =>
                (StatusCodes.Status503ServiceUnavailable, "Service temporarily unavailable", "A transient persistence failure prevented this request from completing."),
            ArgumentException when exception is not ArgumentNullException =>
                (StatusCodes.Status400BadRequest, "Invalid request", "One or more supplied values are invalid."),
            _ =>
                (StatusCodes.Status500InternalServerError, "Internal server error", "An unexpected error occurred.")
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API request failure.");
        }
        else
        {
            logger.LogInformation("API request failed with status {StatusCode}: {Title}.", status, title);
        }

        httpContext.Response.StatusCode = status;
        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = "2";
        }

        ProblemDetails problemDetails;
        if (exception is ArgumentException argumentException &&
            exception is not ArgumentNullException)
        {
            string key = string.IsNullOrWhiteSpace(argumentException.ParamName)
                ? "request"
                : argumentException.ParamName;
            problemDetails = new ValidationProblemDetails(
                new Dictionary<string, string[]> { [key] = [detail] })
            {
                Status = status,
                Title = title,
                Instance = httpContext.Request.Path
            };
        }
        else
        {
            problemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            };
        }

        if (exception is MissingMarketPricesException missingPricesException)
        {
            problemDetails.Extensions["missingInstruments"] = missingPricesException.MissingInstruments
                .Select(instrument => new
                {
                    instrument.InstrumentId,
                    instrument.Symbol
                })
                .ToArray();
        }

        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            contentType: "application/problem+json",
            options: null,
            cancellationToken: cancellationToken);
        return true;
    }
}
