using BuildWorkflow.Models;

namespace BuildWorkflow.AICenter.Tools;

public interface IToolProvider
{
    string Name { get; }

    bool CanHandle(ToolDefinition tool);

    Task<object?> ExecuteAsync(ToolDefinition tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default);
}
