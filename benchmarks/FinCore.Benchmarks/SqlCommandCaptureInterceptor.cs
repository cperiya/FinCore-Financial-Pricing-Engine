using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinCore.Benchmarks;

internal sealed class SqlCommandCaptureInterceptor : DbCommandInterceptor
{
    private readonly List<SqlCommandDefinition> _commands = [];

    public IReadOnlyList<SqlCommandDefinition> Commands => _commands.ToArray();

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _commands.Add(SqlCommandDefinition.From(command));
        return ValueTask.FromResult(result);
    }
}
