using FinCore.Application.Persistence;

namespace FinCore.Infrastructure.Persistence;

internal static class SqlExceptionTranslator
{
    private const int DeadlockErrorNumber = 1205;
    private const int SqlCommandTimeoutErrorNumber = -2;
    private const int TcpWaitTimeoutErrorNumber = 258;
    private const byte ConnectionFailureSeverity = 20;

    internal static Exception Translate(
        int errorNumber,
        byte errorClass,
        Exception providerException,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(providerException);

        if (errorNumber == DeadlockErrorNumber)
        {
            return new TransactionDeadlockException(providerException);
        }

        if (cancellationToken.IsCancellationRequested &&
            (errorNumber == SqlCommandTimeoutErrorNumber ||
             (errorNumber == TcpWaitTimeoutErrorNumber && errorClass >= ConnectionFailureSeverity)))
        {
            return new OperationCanceledException(
                "SQL Server operation was canceled.",
                providerException,
                cancellationToken);
        }

        if (errorNumber == SqlCommandTimeoutErrorNumber)
        {
            return new TimeoutException("SQL Server operation timed out.", providerException);
        }

        if (errorNumber == TcpWaitTimeoutErrorNumber && errorClass >= ConnectionFailureSeverity)
        {
            return new TransientPersistenceException(providerException);
        }

        return providerException;
    }
}
