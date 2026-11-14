using FinCore.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace FinCore.Api.Controllers;

/// <summary>
/// Provides API discovery information at the root URL.
/// </summary>
[ApiController]
public sealed class HomeController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    /// <summary>
    /// Initializes the root controller with the current hosting environment.
    /// </summary>
    /// <param name="environment">The host environment used to determine available API documentation.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="environment"/> is <see langword="null"/>.
    /// </exception>
    public HomeController(IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    /// <summary>
    /// Returns the application description and links to available API resources.
    /// </summary>
    /// <returns>Application metadata and relative resource links.</returns>
    /// <response code="200">The API discovery document is returned.</response>
    [HttpGet("/")]
    [ProducesResponseType(typeof(ApiHomeResponse), StatusCodes.Status200OK)]
    public ActionResult<ApiHomeResponse> Get()
    {
        Dictionary<string, string> links = new()
        {
            ["stockValuation"] = "/api/pricing/stock?symbol={symbol}&unitPrice={unitPrice}&quantity={quantity}"
        };

        if (_environment.IsDevelopment())
        {
            links["openApi"] = "/openapi/v1.json";
        }

        return Ok(new ApiHomeResponse(
            "FinCore",
            "Financial pricing API for stock position market valuation.",
            links));
    }
}
