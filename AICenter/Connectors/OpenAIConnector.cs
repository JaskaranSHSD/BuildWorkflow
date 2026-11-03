using BuildWorkflow.Models;
using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;
using OpenAI;
using System.ClientModel;

namespace BuildWorkflow.AICenter.Connectors;

public class OpenAIConnector : IAiChatConnector
{
    private readonly IConfiguration _configuration;

    public OpenAIConnector(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Provider => "OpenAI";

    public async Task<string> ChatAsync(AiModelDefinition model, IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, IList<Microsoft.Extensions.AI.AITool>? tools = null, CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey(model);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is not configured. Set OpenAI:ApiKey in appsettings or OPENAI_API_KEY environment variable.");
        }

        OpenAIClient client;
        var endpoint = string.IsNullOrWhiteSpace(model.Endpoint) ? null : model.Endpoint.Trim();
        var openAiEndpoint = _configuration["OpenAI:Endpoint"];
        var baseUrl = !string.IsNullOrWhiteSpace(endpoint) && !endpoint.Equals("http://localhost:11434", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : (!string.IsNullOrWhiteSpace(openAiEndpoint) ? openAiEndpoint : null);

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        }
        else
        {
            client = new OpenAIClient(apiKey);
        }

        var chatClient = client.GetChatClient(model.ModelName).AsIChatClient();
        var agent = new ChatClientAgent(
            chatClient,
            instructions: model.SystemPrompt ?? "You are a helpful AI assistant. Answer concisely and accurately.",
            name: model.Name,
            tools: tools ?? new List<Microsoft.Extensions.AI.AITool>());

        var session = await agent.CreateSessionAsync(cancellationToken);
        var response = await agent.RunAsync(messages, session, cancellationToken: cancellationToken);
        return response.Text;
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(AiModelDefinition model, IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, IList<Microsoft.Extensions.AI.AITool>? tools = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("OpenAI API key is not configured.");

        OpenAIClient client;
        var endpoint = string.IsNullOrWhiteSpace(model.Endpoint) ? null : model.Endpoint.Trim();
        var openAiEndpoint = _configuration["OpenAI:Endpoint"];
        var baseUrl = !string.IsNullOrWhiteSpace(endpoint) && !endpoint.Equals("http://localhost:11434", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : (!string.IsNullOrWhiteSpace(openAiEndpoint) ? openAiEndpoint : null);

        if (!string.IsNullOrWhiteSpace(baseUrl))
            client = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri(baseUrl) });
        else
            client = new OpenAIClient(apiKey);

        var chatClient = client.GetChatClient(model.ModelName).AsIChatClient();
        var agent = new ChatClientAgent(
            chatClient,
            instructions: model.SystemPrompt ?? "You are a helpful AI assistant. Answer concisely and accurately.",
            name: model.Name,
            tools: tools ?? new List<Microsoft.Extensions.AI.AITool>());

        var session = await agent.CreateSessionAsync(cancellationToken);
        await foreach (var update in agent.RunStreamingAsync(messages, session, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return update.Text;
        }
    }
    private string? ResolveApiKey(AiModelDefinition model)
    {
        // Allow per-model override if an ApiKey property were added in future; fallback to config/env.
        var fromConfig = _configuration["OpenAI:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fromConfig)) return fromConfig;
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    }
}