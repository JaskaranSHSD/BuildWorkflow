using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace BuildWorkflow.AICenter;

public class AICenterService
{
    private readonly AppDbContext _db;
    private readonly AiConnectorFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly Tools.DatabaseToolExecutor _toolExecutor;

    public AICenterService(AppDbContext db, AiConnectorFactory factory, IConfiguration configuration, Tools.DatabaseToolExecutor toolExecutor)
    {
        _db = db;
        _factory = factory;
        _configuration = configuration;
        _toolExecutor = toolExecutor;
    }

    public async Task<AiChatResult> SendMessageAsync(Guid? userId, Guid? modelId, string message, IEnumerable<Guid>? attachedFileIds = null, Guid? sessionId = null, Guid? ragBucketId = null, IEnumerable<Guid>? selectedToolIds = null, IEnumerable<Guid>? selectedRagBucketIds = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message is required.", nameof(message));
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

        var originalMessage = message.Trim();

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
            selectedBucketIds = selectedBucketIds.Where(id => selectedSet.Contains(id)).ToList();
        }

        var maxChunksToScan = _configuration.GetValue<int?>("Rag:MaxChunksToScan") ?? 1000;
        if (maxChunksToScan <= 0)
        {
            maxChunksToScan = 1000;
        }

        // Avoid loading the entire corpus into memory while still scanning enough
        // chunks for a useful approximate nearest-neighbor ranking.
        var ragChunks = await _db.RagChunks
            .AsNoTracking()
            .Where(x => selectedBucketIds.Contains(x.RagBucketId))
            .OrderByDescending(x => x.CreatedAt)
            .Take(maxChunksToScan)
            .ToListAsync(cancellationToken);

        var endpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
        var embeddingModel = _configuration["Ollama:EmbeddingModel"];
        var queryEmbedding = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, originalMessage, cancellationToken);

        IEnumerable<RagChunk> selectedChunks = ragChunks;
        if (queryEmbedding != null && ragChunks.Any(c => c.Embedding != null))
        {
            selectedChunks = ragChunks
                .Select(c => new { Chunk = c, Score = BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.CosineSimilarity(queryEmbedding, (c.Embedding ?? Array.Empty<double>()).Select(v => (float)v).ToArray()) })
                .OrderByDescending(x => x.Score)
                .Take(8)
                .Select(x => x.Chunk);
        }
        else
        {
            selectedChunks = ragChunks;
        }

        var ragContext = BuildWorkflow.RAGPipeline.RagContextBuilder.Build(originalMessage, selectedChunks.Select(x => x.Content));

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
                    throw new ArgumentException($"This chat has reached the maximum of {maxMessages} messages. Please start a new chat.");
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

        var connector = _factory.GetConnector(aiModel);
        Tools.DatabaseToolExecutor.BeginGeneratedFileTracking();
        string reply;
        var generatedFileTokens = new List<string>();
        try
        {
            reply = await connector.ChatAsync(aiModel, history, allowedTools, cancellationToken);
        }
        finally
        {
            generatedFileTokens = Tools.DatabaseToolExecutor.GetGeneratedFileTokens().ToList();
            Tools.DatabaseToolExecutor.ClearGeneratedFileTracking();
        }

        foreach (var token in generatedFileTokens)
        {
            if (!string.IsNullOrWhiteSpace(token) && !(reply?.Contains(token, StringComparison.Ordinal) ?? false))
            {
                reply = (reply?.TrimEnd() ?? string.Empty) + "\n" + token;
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
                if (!(reply?.Contains(token, StringComparison.Ordinal) ?? false))
                {
                    reply = (reply?.TrimEnd() ?? string.Empty) + "\n" + token;
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

        return new AiChatResult(reply, chatSession.Id, aiModel.Name);
    }
}

public record AiChatResult(string Reply, Guid SessionId, string ModelName);