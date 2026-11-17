namespace FinCore.Application.Persistence;

/// <summary>
/// Reports a recognized transient failure while accessing persistent storage.
/// </summary>
public sealed class TransientPersistenceException : Exception
{
    /// <summary>
    /// Initializes a transient persistence exception with its underlying provider failure.
    /// </summary>
    /// <param name="innerException">The provider exception that caused the transient failure.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="innerException"/> is null.</exception>
    public TransientPersistenceException(Exception innerException)
        : base(
            "A transient persistence failure occurred.",
            innerException ?? throw new ArgumentNullException(nameof(innerException)))
    {
    }
}
