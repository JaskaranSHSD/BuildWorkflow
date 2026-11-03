using BuildWorkflow.Models;

namespace BuildWorkflow.AICenter;

public interface IAiChatConnector
{
    string Provider { get; }

    Task<string> ChatAsync(AiModelDefinition model, IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, IList<Microsoft.Extensions.AI.AITool>? tools = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> ChatStreamAsync(AiModelDefinition model, IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, IList<Microsoft.Extensions.AI.AITool>? tools = null, CancellationToken cancellationToken = default);
}