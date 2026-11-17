namespace FinCore.Application.Persistence;

/// <summary>
/// Defines persistence commit and transaction boundaries for an application use case.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Persists all changes tracked by the current unit of work.
    /// </summary>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The number of state entries written to the data store.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs an operation and commits its tracked changes in a serializable transaction.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">The use-case operation to execute within the transaction.</param>
    /// <param name="cancellationToken">A token that can cancel the operation.</param>
    /// <returns>The operation's result after its changes have been committed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
    /// <exception cref="TransientPersistenceException">
    /// Thrown when SQL Server reports a recognized transient connectivity failure.
    /// </exception>
    /// <remarks>
    /// Serializable isolation is available for workflows that must validate persisted state and write
    /// changes atomically, such as validating a sell against the current trade history before inserting it.
    /// </remarks>
    Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs a read-only operation in a serializable transaction without saving tracked changes.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="operation">The read-only operation to execute within the transaction.</param>
    /// <param name="cancellationToken">A token that can cancel the operation or transaction.</param>
    /// <returns>The operation's result after the transaction completes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
    /// <exception cref="TransactionDeadlockException">
    /// Thrown when SQL Server selects the transaction as a deadlock victim.
    /// </exception>
    /// <exception cref="TimeoutException">
    /// Thrown when SQL Server reports a command timeout not caused by cancellation.
    /// </exception>
    /// <exception cref="TransientPersistenceException">
    /// Thrown when SQL Server reports a recognized transient connectivity failure.
    /// </exception>
    /// <remarks>
    /// Use this boundary when multiple reads must observe a serializable state but the use case must not write.
    /// </remarks>
    Task<TResult> ExecuteReadOnlyInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs an operation in a serializable transaction after acquiring transaction-owned exclusive locks.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="lockResources">Logical SQL Server application-lock resource names.</param>
    /// <param name="operation">The use-case operation to execute within the transaction.</param>
    /// <param name="cancellationToken">A token that can cancel lock acquisition or the operation.</param>
    /// <returns>The operation's result after its changes have been committed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="lockResources"/> or <paramref name="operation"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a lock resource is blank or exceeds SQL Server's 255-character resource limit.
    /// </exception>
    /// <exception cref="TimeoutException">Thrown when SQL Server cannot acquire a lock before its timeout.</exception>
    /// <exception cref="TransactionLockAcquisitionException">
    /// Thrown when SQL Server reports another unsuccessful application-lock result.
    /// </exception>
    /// <exception cref="TransactionDeadlockException">
    /// Thrown when SQL Server selects the transaction as a deadlock victim.
    /// </exception>
    /// <exception cref="TransientPersistenceException">
    /// Thrown when SQL Server reports a recognized transient connectivity failure.
    /// </exception>
    /// <remarks>
    /// Infrastructure acquires distinct resource names in ordinal order using transaction ownership.
    /// Lock acquisition, repository operations, saving, and commit use the same transaction.
    /// </remarks>
    Task<TResult> ExecuteWithExclusiveLocksAsync<TResult>(
        IReadOnlyCollection<string> lockResources,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken);
}
