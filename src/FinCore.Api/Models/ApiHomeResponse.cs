namespace FinCore.Api.Models;

/// <summary>
/// Describes the FinCore API and provides links to available resources.
/// </summary>
public sealed record ApiHomeResponse
{
    /// <summary>
    /// Gets the application name.
    /// </summary>
    public string ApplicationName { get; }

    /// <summary>
    /// Gets a brief description of the application.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets relative links to API resources available in the current environment.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; }

    /// <summary>
    /// Initializes a response describing the API and its available links.
    /// </summary>
    /// <param name="applicationName">The application name.</param>
    /// <param name="description">A brief application description.</param>
    /// <param name="links">Relative links keyed by resource name.</param>
    public ApiHomeResponse(
        string applicationName,
        string description,
        IReadOnlyDictionary<string, string> links)
    {
        ApplicationName = applicationName;
        Description = description;
        Links = links;
    }
}
