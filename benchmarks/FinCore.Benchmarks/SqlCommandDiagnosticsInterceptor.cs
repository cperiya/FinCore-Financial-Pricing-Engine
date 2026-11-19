using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinCore.Benchmarks;

internal sealed class SqlCommandDiagnosticsInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<DbCommand, long> _startTimestamps = new();
    private readonly ConcurrentDictionary<DbCommand, string?> _commandStages = new();
    private readonly ConcurrentDictionary<string, SqlStageTotals> _stageTotals =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SqlCommandDefinition> _commands =
        new(StringComparer.Ordinal);
    private long _commandCount;
    private long _elapsedTicks;

    public void Reset()
    {
        _startTimestamps.Clear();
        _commandStages.Clear();
        _stageTotals.Clear();
        _commands.Clear();
        Interlocked.Exchange(ref _commandCount, 0);
        Interlocked.Exchange(ref _elapsedTicks, 0);
    }

    public SqlCommandDiagnosticsSnapshot Snapshot()
    {
        return new SqlCommandDiagnosticsSnapshot(
            Interlocked.Read(ref _commandCount),
            TimeSpan.FromSeconds(Interlocked.Read(ref _elapsedTicks) / (double)Stopwatch.Frequency));
    }

    public SqlCommandDiagnosticsSnapshot Snapshot(string stageName)
    {
        return _stageTotals.TryGetValue(stageName, out SqlStageTotals? totals)
            ? totals.Snapshot()
            : new SqlCommandDiagnosticsSnapshot(0, TimeSpan.Zero);
    }

    public IReadOnlyList<SqlCommandDefinition> Commands =>
        _commands.Values.ToArray();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _startTimestamps[command] = Stopwatch.GetTimestamp();
        _commandStages[command] = BenchmarkDiagnosticExecutionContext.StageName;
        _commands.TryAdd(
            command.CommandText,
            SqlCommandDefinition.From(command));
        Interlocked.Increment(ref _commandCount);
        return ValueTask.FromResult(result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(command);
        return ValueTask.FromResult(result);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RecordDuration(command);
        return Task.CompletedTask;
    }

    private void RecordDuration(DbCommand command)
    {
        if (_startTimestamps.TryRemove(command, out long startedAt))
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startedAt;
            Interlocked.Add(ref _elapsedTicks, elapsedTicks);
            if (_commandStages.TryRemove(command, out string? stageName) &&
                stageName is not null)
            {
                SqlStageTotals totals = _stageTotals.GetOrAdd(
                    stageName,
                    static _ => new SqlStageTotals());
                totals.Add(elapsedTicks);
            }
        }
    }
}

internal sealed record SqlCommandDiagnosticsSnapshot(long ReaderCommandCount, TimeSpan ReaderExecuteDuration);

internal sealed class SqlStageTotals
{
    private long _commandCount;
    private long _elapsedTicks;

    public void Add(long elapsedTicks)
    {
        Interlocked.Increment(ref _commandCount);
        Interlocked.Add(ref _elapsedTicks, elapsedTicks);
    }

    public SqlCommandDiagnosticsSnapshot Snapshot()
    {
        return new SqlCommandDiagnosticsSnapshot(
            Interlocked.Read(ref _commandCount),
            TimeSpan.FromSeconds(Interlocked.Read(ref _elapsedTicks) / (double)Stopwatch.Frequency));
    }
}
