using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using FinCore.Application.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FinCore.Infrastructure.Persistence;

/// <summary>
/// Implements persistence commits and serializable transaction boundaries with EF Core.
/// </summary>
internal sealed class FinCoreUnitOfWork : IUnitOfWork
{
    private const int ApplicationLockTimeoutMilliseconds = 15_000;
    private const int MaximumApplicationLockResourceLength = 255;

    private readonly FinCoreDbContext _dbContext;

    /// <summary>
    /// Initializes a unit of work using the scoped persistence context.
    /// </summary>
    /// <param name="dbContext">The context whose changes are committed.</param>
    public FinCoreUnitOfWork(FinCoreDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return await ExecuteInTransactionCoreAsync([], operation, saveChanges: true, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TResult> ExecuteReadOnlyInTransactionAsync<TResult>(
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return ExecuteInTransactionCoreAsync([], operation, saveChanges: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<TResult> ExecuteWithExclusiveLocksAsync<TResult>(
        IReadOnlyCollection<string> lockResources,
        Func<CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lockResources);
        ArgumentNullException.ThrowIfNull(operation);

        string[] orderedResources = lockResources
            .Select(ValidateLockResource)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(resource => resource, StringComparer.Ordinal)
            .ToArray();

        return await ExecuteInTransactionCoreAsync(
            orderedResources,
            operation,
            saveChanges: true,
            cancellationToken);
    }

    private async Task<TResult> ExecuteInTransactionCoreAsync<TResult>(
        IReadOnlyList<string> lockResources,
        Func<CancellationToken, Task<TResult>> operation,
        bool saveChanges,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction transaction;
        try
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
        }
        catch (Exception exception)
        {
            Exception translatedException = TranslateSqlException(exception, cancellationToken);
            if (!ReferenceEquals(translatedException, exception))
            {
                ExceptionDispatchInfo.Capture(translatedException).Throw();
            }

            throw;
        }

        await using IDbContextTransaction transactionToDispose = transaction;

        try
        {
            foreach (string resource in lockResources)
            {
                await AcquireApplicationLockAsync(transaction, resource, cancellationToken);
            }

            TResult result = await operation(cancellationToken);
            if (saveChanges)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (Exception transactionException)
        {
            Exception failure = TranslateSqlException(transactionException, cancellationToken);

            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                AggregateException combinedFailure = new(
                    "The database operation failed and SQL Server rollback also failed.",
                    failure,
                    rollbackException);
                if (IsRetryablePersistenceFailure(failure))
                {
                    throw new TransientPersistenceException(combinedFailure);
                }

                throw combinedFailure;
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    private static Exception TranslateSqlException(
        Exception exception,
        CancellationToken cancellationToken)
    {
        SqlException? sqlException = FindSqlException(exception);
        return sqlException is null
            ? exception
            : SqlExceptionTranslator.Translate(
                sqlException.Number,
                sqlException.Class,
                exception,
                cancellationToken);
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        if (exception is SqlException sqlException)
        {
            return sqlException;
        }

        if (exception is AggregateException aggregateException)
        {
            foreach (Exception innerException in aggregateException.Flatten().InnerExceptions)
            {
                SqlException? nestedSqlException = FindSqlException(innerException);
                if (nestedSqlException is not null)
                {
                    return nestedSqlException;
                }
            }
        }

        return exception.InnerException is null
            ? null
            : FindSqlException(exception.InnerException);
    }

    private static bool IsRetryablePersistenceFailure(Exception exception)
    {
        return exception is TimeoutException or
            TransactionLockAcquisitionException or
            TransactionDeadlockException or
            TransientPersistenceException;
    }

    private async Task AcquireApplicationLockAsync(
        IDbContextTransaction transaction,
        string resource,
        CancellationToken cancellationToken)
    {
        await using DbCommand command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @LockResult int;
            EXEC @LockResult = sys.sp_getapplock
                @Resource = @ResourceName,
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = @LockTimeout,
                @DbPrincipal = N'public';
            SELECT @LockResult;
            """;

        DbParameter resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@ResourceName";
        resourceParameter.DbType = DbType.String;
        resourceParameter.Size = MaximumApplicationLockResourceLength;
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        DbParameter timeoutParameter = command.CreateParameter();
        timeoutParameter.ParameterName = "@LockTimeout";
        timeoutParameter.DbType = DbType.Int32;
        timeoutParameter.Value = ApplicationLockTimeoutMilliseconds;
        command.Parameters.Add(timeoutParameter);

        object? result;
        try
        {
            result = await command.ExecuteScalarAsync(cancellationToken);
        }
        catch (SqlException exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                $"SQL Server application-lock acquisition was canceled for resource '{resource}'.",
                exception,
                cancellationToken);
        }

        if (result is not int resultCode)
        {
            throw new InvalidOperationException(
                $"SQL Server returned no application-lock result for resource '{resource}'.");
        }

        if (resultCode >= 0)
        {
            return;
        }

        if (resultCode == -1)
        {
            throw new TimeoutException(
                $"SQL Server timed out acquiring application lock '{resource}' after " +
                $"{ApplicationLockTimeoutMilliseconds} milliseconds.");
        }

        if (resultCode == -2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new OperationCanceledException(
                $"SQL Server canceled application-lock acquisition for resource '{resource}'.",
                cancellationToken);
        }

        throw new TransactionLockAcquisitionException(resource, resultCode);
    }

    private static string ValidateLockResource(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (resource.Length > MaximumApplicationLockResourceLength)
        {
            throw new ArgumentException(
                $"SQL Server application-lock resource names cannot exceed " +
                $"{MaximumApplicationLockResourceLength} characters.",
                nameof(resource));
        }

        return resource;
    }
}
