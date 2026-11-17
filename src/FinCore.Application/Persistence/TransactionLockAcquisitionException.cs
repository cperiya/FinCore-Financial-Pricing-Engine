namespace FinCore.Application.Persistence;

/// <summary>
/// Reports an unsuccessful SQL Server application-lock acquisition.
/// </summary>
public sealed class TransactionLockAcquisitionException : InvalidOperationException
{
    /// <summary>
    /// Initializes an exception for an unsuccessful SQL Server application-lock result.
    /// </summary>
    /// <param name="resourceName">The logical lock resource that could not be acquired.</param>
    /// <param name="resultCode">The negative result code returned by SQL Server.</param>
    public TransactionLockAcquisitionException(string resourceName, int resultCode)
        : base($"SQL Server could not acquire application lock '{resourceName}' (result {resultCode}).")
    {
        ResourceName = resourceName;
        ResultCode = resultCode;
    }

    /// <summary>
    /// Gets the logical lock resource that could not be acquired.
    /// </summary>
    public string ResourceName { get; }

    /// <summary>
    /// Gets the negative result code returned by SQL Server.
    /// </summary>
    public int ResultCode { get; }
}
