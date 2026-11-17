namespace FinCore.Application.Persistence;

/// <summary>
/// Reports that SQL Server selected the current transaction as a deadlock victim.
/// </summary>
public sealed class TransactionDeadlockException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for a SQL Server deadlock victim.
    /// </summary>
    /// <param name="innerException">The SQL Server exception with deadlock details.</param>
    public TransactionDeadlockException(Exception innerException)
        : base(
            "SQL Server selected the transaction as a deadlock victim; the operation was rolled back. " +
            "A caller may retry using the same trade identifier.",
            innerException)
    {
        ArgumentNullException.ThrowIfNull(innerException);
    }
}
