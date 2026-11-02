using BuildWorkflow.Models;
using Microsoft.Agents.AI;
using OllamaSharp;

namespace BuildWorkflow.AICenter.Connectors;

public class OllamaConnector : IAiChatConnector
{
    public string Provider => "Ollama";

    public async Task<string> ChatAsync(AiModelDefinition model, IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, IList<Microsoft.Extensions.AI.AITool>? tools = null, CancellationToken cancellationToken = default)
    {
        var client = new OllamaApiClient(new Uri(model.Endpoint), model.ModelName);
        var agent = new ChatClientAgent(
            client,
            instructions: model.SystemPrompt ?? "You are a helpful AI assistant. Answer concisely and accurately.",
            name: model.Name,
            tools: tools ?? new List<Microsoft.Extensions.AI.AITool>());

        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(messages, session, cancellationToken: cancellationToken);
        return response.Text;
    }
}