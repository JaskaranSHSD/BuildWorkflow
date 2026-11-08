using BuildWorkflow.Data;
using BuildWorkflow.Models;
using BuildWorkflow.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using System.Text;

namespace BuildWorkflow.AICenter;

public record AiChatResult(string Reply, Guid SessionId, string ModelName);

public record AiChatStreamChunk(string? Delta, Guid? SessionId, string? ModelName, bool Done, string? Error = null);

public class AICenterService
{
    private readonly AppDbContext _db;
    private readonly AiConnectorFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly Tools.DatabaseToolExecutor _toolExecutor;
    private readonly QdrantRagStore _qdrant;

    public AICenterService(AppDbContext db, AiConnectorFactory factory, IConfiguration configuration, Tools.DatabaseToolExecutor toolExecutor, QdrantRagStore qdrant)
    {
        _db = db;
        _factory = factory;
        _configuration = configuration;
        _toolExecutor = toolExecutor;
        _qdrant = qdrant;
    }

    public async Task<AiChatResult> SendMessageAsync(Guid? userId, Guid? modelId, string message, IEnumerable<Guid>? attachedFileIds = null, Guid? sessionId = null, Guid? ragBucketId = null, IEnumerable<Guid>? selectedToolIds = null, IEnumerable<Guid>? selectedRagBucketIds = null, CancellationToken cancellationToken = default)
    {
        var replyBuilder = new StringBuilder();
        Guid resolvedSessionId = Guid.Empty;
        string resolvedModelName = string.Empty;

        await foreach (var chunk in SendMessageStreamAsync(userId, modelId, message, attachedFileIds, sessionId, ragBucketId, selectedToolIds, selectedRagBucketIds, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(chunk.Delta))
            {
                replyBuilder.Append(chunk.Delta);
            }

            if (chunk.SessionId.HasValue)
            {
                resolvedSessionId = chunk.SessionId.Value;
            }

            if (!string.IsNullOrWhiteSpace(chunk.ModelName))
            {
                resolvedModelName = chunk.ModelName!;
            }
        }

        return new AiChatResult(replyBuilder.ToString(), resolvedSessionId, resolvedModelName);
    }

    public async IAsyncEnumerable<AiChatStreamChunk> SendMessageStreamAsync(Guid? userId, Guid? modelId, string message, IEnumerable<Guid>? attachedFileIds = null, Guid? sessionId = null, Guid? ragBucketId = null, IEnumerable<Guid>? selectedToolIds = null, IEnumerable<Guid>? selectedRagBucketIds = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message) && (attachedFileIds == null || !attachedFileIds.Any()))
        {
            yield return new AiChatStreamChunk(null, null, null, true, "Message is required.");
            yield break;
        }

        AiModelDefinition? aiModel = null;
        if (modelId.HasValue)
        {
            aiModel = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == modelId.Value && m.IsActive, cancellationToken);
        }

        if (aiModel == null)
        {
            var defaultModelName = _configuration["Ollama:Model"] ?? "beam:cloud";
            var defaultEndpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
            aiModel = new AiModelDefinition
            {
                Name = "Default",
                Provider = "Ollama",
                Endpoint = defaultEndpoint,
                ModelName = defaultModelName
            };
        }

        var effectiveToolIds = (aiModel?.SupportsTools ?? true) ? selectedToolIds : Array.Empty<Guid>();
        var allowedTools = (await _toolExecutor.GetAllowedToolsAsync(userId, effectiveToolIds, cancellationToken)).ToList();
        var requestStart = DateTimeOffset.UtcNow;

        var originalMessage = message?.Trim() ?? string.Empty;

        var attachedIds = (attachedFileIds ?? Enumerable.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        string tokenInstruction = string.Empty;
        var attachedFileNames = new List<string>();
        if (userId.HasValue && attachedIds.Count > 0)
        {
            var tokens = string.Join(", ", attachedIds.Select(id => $"file:{id}"));
            tokenInstruction = $"\n\nAttached file tokens for this message only: {tokens}. Use read_attached_file with one of those tokens if needed.";
            attachedFileNames = await _db.UploadedFiles
                .Where(f => attachedIds.Contains(f.Id))
                .Select(f => f.OriginalFileName)
                .ToListAsync(cancellationToken);
        }

        var cleanMessage = originalMessage;
        if (attachedFileNames.Count > 0)
        {
            var names = string.Join(", ", attachedFileNames);
            cleanMessage = string.IsNullOrWhiteSpace(originalMessage)
                ? $"Sent attached file{(attachedFileNames.Count > 1 ? "s" : "")}: {names}"
                : $"{originalMessage}\n\nAttached files: {names}";
        }

        var selectedBucketIds = new List<Guid>();
        if (ragBucketId.HasValue)
        {
            selectedBucketIds.Add(ragBucketId.Value);
        }
        else if (userId.HasValue)
        {
            selectedBucketIds = await _db.UserRagBuckets
                .Where(x => x.UserId == userId.Value)
                .Select(x => x.RagBucketId)
                .ToListAsync(cancellationToken);
            if (selectedBucketIds.Count == 0)
            {
                selectedBucketIds = await _db.RagBuckets
                    .Where(x => x.IsActive)
                    .Select(x => x.Id)
                    .ToListAsync(cancellationToken);
            }
        }
        else
        {
            selectedBucketIds = await _db.RagBuckets
                .Where(x => x.IsActive)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
        }

        if (selectedRagBucketIds != null)
        {
            var selectedSet = selectedRagBucketIds.Where(id => id != Guid.Empty).ToHashSet();
            if (selectedSet.Count > 0)
            {
                selectedBucketIds = selectedBucketIds.Where(id => selectedSet.Contains(id)).ToList();
            }
        }

        var maxChunksToScan = _configuration.GetValue<int?>("Rag:MaxChunksToScan") ?? 1000;
        if (maxChunksToScan <= 0)
        {
            maxChunksToScan = 1000;
        }

        var endpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
        string? embeddingModel = null;
        var embeddingBucket = await _db.RagBuckets.FirstOrDefaultAsync(b => selectedBucketIds.Contains(b.Id) && b.EmbeddingModelId.HasValue, cancellationToken);
        if (embeddingBucket?.EmbeddingModelId.HasValue == true)
        {
            var selectedAiModel = await _db.AiModels.FirstOrDefaultAsync(m => m.Id == embeddingBucket.EmbeddingModelId.Value, cancellationToken);
            if (selectedAiModel != null)
            {
                endpoint = string.IsNullOrWhiteSpace(selectedAiModel.Endpoint) ? endpoint : selectedAiModel.Endpoint;
                embeddingModel = selectedAiModel.ModelName;
            }
        }
        else
        {
            embeddingModel = _configuration["Ollama:EmbeddingModel"];
        }
        var queryEmbedding = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, originalMessage, cancellationToken);

        var maxChunks = maxChunksToScan <= 0 ? 1000 : maxChunksToScan;
        float[]? qVector = queryEmbedding;
        var selectedContents = await _qdrant.SearchAsync(selectedBucketIds, originalMessage, qVector, maxChunks, cancellationToken);

        var ragContext = BuildWorkflow.RAGPipeline.RagContextBuilder.Build(originalMessage, selectedContents);

        ChatSession chatSession;
        if (sessionId.HasValue)
        {
            var existing = await _db.ChatSessions
                .Include(s => s.Messages)
                .FirstOrDefaultAsync(s => s.Id == sessionId.Value && (userId == null || s.UserId == userId), cancellationToken);

            if (existing == null)
            {
                chatSession = new ChatSession { UserId = userId, AgentName = aiModel.Name, Title = originalMessage.Length > 60 ? originalMessage[..60] + "..." : originalMessage };
                _db.ChatSessions.Add(chatSession);
            }
            else
            {
                var maxMessages = _configuration.GetValue<int?>("Chat:MaxMessagesPerSession") ?? 50;
                if (existing.Messages.Count >= maxMessages)
                {
                    yield return new AiChatStreamChunk(null, null, null, true, $"This chat has reached the maximum of {maxMessages} messages. Please start a new chat.");
                    yield break;
                }
                chatSession = existing;
            }
        }
        else
        {
            chatSession = new ChatSession { UserId = userId, AgentName = aiModel.Name, Title = originalMessage.Length > 60 ? originalMessage[..60] + "..." : originalMessage };
            _db.ChatSessions.Add(chatSession);
        }

        var history = new List<ChatMessage>();
        var historicalFileIds = new HashSet<Guid>();
        foreach (var messageRecord in chatSession.Messages.OrderBy(m => m.CreatedAt))
        {
            var content = messageRecord.Content;
            if (messageRecord.Role != "assistant" && !string.IsNullOrWhiteSpace(messageRecord.AttachedFileIds))
            {
                var ids = messageRecord.AttachedFileIds
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                    .Where(g => g != Guid.Empty)
                    .ToList();

                foreach (var id in ids)
                {
                    historicalFileIds.Add(id);
                }

                if (ids.Count > 0)
                {
                    var tokens = string.Join(", ", ids.Select(id => $"file:{id}"));
                    content = $"{content}\n\nAttached file tokens for that message only: {tokens}. Use read_attached_file with one of those tokens if needed.";
                }
            }

            history.Add(new ChatMessage(messageRecord.Role == "assistant" ? ChatRole.Assistant : ChatRole.User, content));
        }

        var allFileIds = attachedIds.Concat(historicalFileIds).Distinct().ToList();
        if (userId.HasValue && allFileIds.Count > 0 && !allowedTools.Any(t => t.Name == "read_attached_file"))
        {
            allowedTools.Add(_toolExecutor.CreateAttachedFileTool(userId.Value, allFileIds));
        }

        var modelMessage = cleanMessage + tokenInstruction;
        if (!string.IsNullOrWhiteSpace(ragContext))
        {
            modelMessage = $"Use the following knowledge to answer the user.\nKnowledge:\n{ragContext}\n\nQuestion: {cleanMessage}{tokenInstruction}";
        }

        history.Add(new ChatMessage(ChatRole.User, modelMessage));

        yield return new AiChatStreamChunk(null, chatSession.Id, aiModel.Name, false);

        var connector = _factory.GetConnector(aiModel);
        Tools.DatabaseToolExecutor.BeginGeneratedFileTracking();
        var replyBuilder = new StringBuilder();
        try
        {
            await foreach (var delta in connector.ChatStreamAsync(aiModel, history, allowedTools, cancellationToken))
            {
                if (!string.IsNullOrEmpty(delta))
                {
                    replyBuilder.Append(delta);
                    yield return new AiChatStreamChunk(delta, null, null, false);
                }
            }
        }
        finally
        {
            // no-op placeholder
        }

        var generatedFileTokens = Tools.DatabaseToolExecutor.GetGeneratedFileTokens().ToList();
        Tools.DatabaseToolExecutor.ClearGeneratedFileTracking();

        var reply = replyBuilder.ToString();

        foreach (var token in generatedFileTokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && !(reply.Contains(token, StringComparison.Ordinal)))
            {
                reply = reply.TrimEnd() + "\n" + token;
                yield return new AiChatStreamChunk("\n" + token, null, null, false);
            }
        }

        if (userId.HasValue)
        {
            var dbTokens = await _db.UploadedFiles
                .Where(f => f.SessionKey == "tool-generated" && f.UserId == userId.Value && f.CreatedAt >= requestStart.AddSeconds(-2))
                .Select(f => $"file:{f.Id}")
                .ToListAsync(cancellationToken);

            foreach (var token in dbTokens)
            {
                if (!(reply.Contains(token, StringComparison.Ordinal)))
                {
                    reply = reply.TrimEnd() + "\n" + token;
                    yield return new AiChatStreamChunk("\n" + token, null, null, false);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(chatSession.Title) || chatSession.Title == "New chat")
        {
            chatSession.Title = originalMessage.Length > 60 ? originalMessage[..60] + "..." : originalMessage;
        }

        chatSession.AgentName = aiModel.Name;
        chatSession.UpdatedAt = DateTimeOffset.UtcNow;

        _db.ChatMessages.Add(new ChatMessageRecord
        {
            ChatSessionId = chatSession.Id,
            Role = "user",
            Content = cleanMessage,
            AttachedFileIds = attachedIds.Count > 0 ? string.Join(",", attachedIds) : null
        });
        _db.ChatMessages.Add(new ChatMessageRecord
        {
            ChatSessionId = chatSession.Id,
            Role = "assistant",
            Content = reply
        });

        await _db.SaveChangesAsync(cancellationToken);

        yield return new AiChatStreamChunk(null, chatSession.Id, aiModel.Name, true);
    }
}
