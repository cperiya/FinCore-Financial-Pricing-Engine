using System.Diagnostics;
using FinCore.Application.Portfolios;

namespace FinCore.Benchmarks;

internal sealed class ValuationDiagnosticsCollector : IPortfolioValuationDiagnostics
{
    private readonly SqlCommandDiagnosticsInterceptor _sqlDiagnostics;
    private readonly List<ValuationDiagnosticStage> _stages = [];

    public ValuationDiagnosticsCollector(SqlCommandDiagnosticsInterceptor sqlDiagnostics)
    {
        _sqlDiagnostics = sqlDiagnostics;
    }

    public IReadOnlyList<ValuationDiagnosticStage> Stages => _stages;

    public IPortfolioValuationDiagnosticStage BeginStage(string stageName, int inputCount)
    {
        return new StageScope(this, stageName, inputCount, Stopwatch.GetTimestamp());
    }

    public void Reset()
    {
        _stages.Clear();
        _sqlDiagnostics.Reset();
    }

    private void Complete(
        string stageName,
        int inputCount,
        int outputCount,
        long startedAt,
        long allocatedAtStart)
    {
        long allocatedAtEnd = GC.GetTotalAllocatedBytes(precise: false);
        long allocatedBytes = Math.Max(0, allocatedAtEnd - allocatedAtStart);
        SqlCommandDiagnosticsSnapshot sql = _sqlDiagnostics.Snapshot(stageName);
        _stages.Add(new ValuationDiagnosticStage(
            stageName,
            inputCount,
            outputCount,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            allocatedBytes,
            sql.ReaderCommandCount,
            sql.ReaderExecuteDuration.TotalMilliseconds));
    }

    private sealed class StageScope : IPortfolioValuationDiagnosticStage
    {
        private readonly ValuationDiagnosticsCollector _owner;
        private readonly string _stageName;
        private readonly int _inputCount;
        private readonly long _startedAt;
        private readonly long _allocatedAtStart = GC.GetTotalAllocatedBytes(precise: false);
        private readonly IDisposable _contextScope;
        private int _completed;

        public StageScope(
            ValuationDiagnosticsCollector owner,
            string stageName,
            int inputCount,
            long startedAt)
        {
            _owner = owner;
            _stageName = stageName;
            _inputCount = inputCount;
            _startedAt = startedAt;
            _contextScope = BenchmarkDiagnosticExecutionContext.Push(stageName);
        }

        public void Complete(int outputCount)
        {
            if (Interlocked.Exchange(ref _completed, 1) == 0)
            {
                _owner.Complete(
                    _stageName,
                    _inputCount,
                    outputCount,
                    _startedAt,
                    _allocatedAtStart);
                _contextScope.Dispose();
            }
        }

        public void Dispose()
        {
            Complete(0);
        }
    }
}

internal sealed record ValuationDiagnosticStage(
    string StageName,
    int InputCount,
    int OutputCount,
    double ElapsedMilliseconds,
    long ApproximateManagedAllocationsBytes,
    long ReaderCommandCount,
    double ReaderExecuteMilliseconds);

internal static class BenchmarkDiagnosticExecutionContext
{
    private static readonly AsyncLocal<string?> CurrentStage = new();

    public static string? StageName => CurrentStage.Value;

    public static IDisposable Push(string stageName)
    {
        string? previous = CurrentStage.Value;
        CurrentStage.Value = stageName;
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly string? _previous;

        public Scope(string? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            CurrentStage.Value = _previous;
        }
    }
}
