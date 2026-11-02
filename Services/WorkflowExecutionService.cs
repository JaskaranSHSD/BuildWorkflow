using BuildWorkflow.AICenter;
using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace BuildWorkflow.Services;

public class WorkflowExecutionService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiConnectorFactory _factory;
    private readonly ILogger<WorkflowExecutionService> _logger;

    public WorkflowExecutionService(IServiceScopeFactory scopeFactory, AiConnectorFactory factory, ILogger<WorkflowExecutionService> logger)
    {
        _scopeFactory = scopeFactory;
        _factory = factory;
        _logger = logger;
    }

    public async Task RunAsync(Guid runId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var toolExecutor = scope.ServiceProvider.GetRequiredService<BuildWorkflow.AICenter.Tools.DatabaseToolExecutor>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        var run = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run == null) return;

        var workflow = await db.Workflows.FirstOrDefaultAsync(w => w.Id == run.WorkflowId, cancellationToken);

        if (workflow == null)
        {
            run.Status = "Failed";
            run.Error = "Workflow not found.";
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        run.Status = "Running";
        run.StartedAt = DateTimeOffset.UtcNow;
        var runStart = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var aiModel = await ResolveModelAsync(db, workflow, configuration, cancellationToken);

            var toolIds = (await db.WorkflowTools.Where(x => x.WorkflowId == workflow.Id).Select(x => x.ToolId).ToListAsync(cancellationToken)).ToHashSet();
            var allAllowed = await toolExecutor.GetAllowedToolsAsync(run.UserId, null, cancellationToken);
            var toolDefs = await db.Tools.Include(t => t.ApiConfiguration).Include(t => t.SqlConfiguration).Include(t => t.SftpConfiguration)
                .Where(t => toolIds.Contains(t.Id)).ToListAsync(cancellationToken);

            var selectedToolNames = toolDefs.Select(t => Sanitize(t.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tools = allAllowed.Where(t => selectedToolNames.Contains(t.Name ?? string.Empty)).ToList();

            List<Guid> fileIds;
            if (run.ParentRunId.HasValue)
            {
                // Downstream workflows source generated files from the parent run instead
                // of maintaining their own separate attachments.
                var parentRunForFiles = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == run.ParentRunId.Value, cancellationToken);
                fileIds = new List<Guid>();
                if (parentRunForFiles?.OutputSummary != null)
                {
                    foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(parentRunForFiles.OutputSummary, @"file:([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})"))
                    {
                        if (Guid.TryParse(match.Groups[1].Value, out var fileId))
                        {
                            fileIds.Add(fileId);
                        }
                    }
                }
            }
            else
            {
                fileIds = await db.WorkflowFiles.Where(f => f.WorkflowId == workflow.Id).Select(f => f.UploadedFileId).ToListAsync(cancellationToken);
            }
            if (run.UserId.HasValue && fileIds.Count > 0)
            {
                tools = tools.ToList();
                tools.Add(toolExecutor.CreateAttachedFileTool(run.UserId.Value, fileIds));
            }

            var session = new WorkflowSession { WorkflowId = workflow.Id, UserId = run.UserId, Title = workflow.Name };
            db.WorkflowSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            run.WorkflowSessionId = session.Id;

            var history = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(workflow.SystemInstruction))
                history.Add(new ChatMessage(ChatRole.System, workflow.SystemInstruction));

            var userMessage = workflow.UserInstruction;
            if (run.ParentRunId.HasValue)
            {
                var parentRun = await db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == run.ParentRunId.Value, cancellationToken);
                if (parentRun != null && !string.IsNullOrWhiteSpace(parentRun.OutputSummary))
                {
                    userMessage = $"Previous workflow result:\n{parentRun.OutputSummary}\n\nCurrent instruction:\n{userMessage}";
                }
            }
            if (fileIds.Count > 0)
            {
                var names = await db.UploadedFiles.Where(f => fileIds.Contains(f.Id)).Select(f => f.OriginalFileName).ToListAsync(cancellationToken);
                if (names.Count > 0)
                    userMessage = (string.IsNullOrWhiteSpace(userMessage) ? "" : userMessage + "\n\n") + "Attached files: " + string.Join(", ", names);
                userMessage += "\n\nAttached file tokens for this run only: " + string.Join(", ", fileIds.Select(id => "file:" + id)) + ". Use read_attached_file with one of those tokens if needed.";
            }
            history.Add(new ChatMessage(ChatRole.User, userMessage));
            db.WorkflowMessages.Add(new WorkflowMessage { WorkflowSessionId = session.Id, Role = "user", Content = workflow.UserInstruction });

            var bucketIds = await db.WorkflowRagBuckets.Where(x => x.WorkflowId == workflow.Id).Select(x => x.RagBucketId).ToListAsync(cancellationToken);
            if (bucketIds.Count == 0 && workflow.RagBucketId.HasValue)
                bucketIds.Add(workflow.RagBucketId.Value);
            if (bucketIds.Count == 0)
            {
                if (run.UserId.HasValue)
                    bucketIds = await db.UserRagBuckets.Where(x => x.UserId == run.UserId.Value).Select(x => x.RagBucketId).ToListAsync(cancellationToken);
                else
                    bucketIds = await db.RagBuckets.Where(x => x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
            }

            var chunks = await db.RagChunks.Where(x => bucketIds.Contains(x.RagBucketId)).ToListAsync(cancellationToken);
            var endpoint = configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
            var embeddingModel = configuration["Ollama:EmbeddingModel"];
            var queryEmbedding = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, workflow.UserInstruction, cancellationToken);
            var selectedChunks = chunks.AsEnumerable();
            if (queryEmbedding != null && chunks.Any(c => c.Embedding != null))
            {
                selectedChunks = chunks.Select(c => new { Chunk = c, Score = BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.CosineSimilarity(queryEmbedding, (c.Embedding ?? Array.Empty<double>()).Select(v => (float)v).ToArray()) })
                    .OrderByDescending(x => x.Score).Take(8).Select(x => x.Chunk);
            }
            var ragContext = BuildWorkflow.RAGPipeline.RagContextBuilder.Build(workflow.UserInstruction, selectedChunks.Select(x => x.Content));
            if (!string.IsNullOrWhiteSpace(ragContext))
            {
                var modelMessage = "Use the following knowledge to answer the user.\nKnowledge:\n" + ragContext + "\n\nQuestion: " + userMessage;
                history.RemoveAt(history.Count - 1);
                history.Add(new ChatMessage(ChatRole.User, modelMessage));
            }

            var connector = _factory.GetConnector(aiModel);
            BuildWorkflow.AICenter.Tools.DatabaseToolExecutor.BeginGeneratedFileTracking();
            string reply;
            var generatedFileTokens = new List<string>();
            try
            {
                reply = await connector.ChatAsync(aiModel, history, tools, cancellationToken);
            }
            finally
            {
                generatedFileTokens = BuildWorkflow.AICenter.Tools.DatabaseToolExecutor.GetGeneratedFileTokens().ToList();
                BuildWorkflow.AICenter.Tools.DatabaseToolExecutor.ClearGeneratedFileTracking();
            }
            foreach (var token in generatedFileTokens)
            {
                if (!string.IsNullOrWhiteSpace(token) && !(reply?.Contains(token, StringComparison.Ordinal) ?? false))
                {
                    reply = (reply?.TrimEnd() ?? string.Empty) + "\n" + token;
                }
            }

            if (run.UserId.HasValue)
            {
                var dbTokens = await db.UploadedFiles
                    .Where(f => f.SessionKey == "tool-generated" && f.UserId == run.UserId.Value && f.CreatedAt >= runStart.AddSeconds(-2))
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

            db.WorkflowMessages.Add(new WorkflowMessage { WorkflowSessionId = session.Id, Role = "assistant", Content = reply });
            run.Status = "Completed";
            run.OutputSummary = reply?.Length > 4000 ? reply.Substring(0, 4000) + "... [truncated]" : reply;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            if (workflow.NextWorkflowId.HasValue)
            {
                var currentDepth = 0;
                var cursorRun = run;
                while (cursorRun.ParentRunId.HasValue)
                {
                    var parent = await db.WorkflowRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == cursorRun.ParentRunId.Value, cancellationToken);
                    if (parent == null) break;
                    cursorRun = parent;
                    currentDepth++;
                    if (currentDepth >= 10) break;
                }

                if (currentDepth < 10)
                {
                    var nextWorkflow = await db.Workflows.FirstOrDefaultAsync(w => w.Id == workflow.NextWorkflowId.Value && w.IsActive, cancellationToken);
                    if (nextWorkflow != null)
                    {
                        var nextRun = new WorkflowRun
                        {
                            WorkflowId = nextWorkflow.Id,
                            UserId = run.UserId,
                            Status = "Queued",
                            ModelId = nextWorkflow.ModelId,
                            ParentRunId = run.Id
                        };
                        db.WorkflowRuns.Add(nextRun);
                        await db.SaveChangesAsync(cancellationToken);

                        try
                        {
                            var queue = scope.ServiceProvider.GetRequiredService<WorkflowRunQueue>();
                            queue.Enqueue(nextRun.Id);
                        }
                        catch (Exception queueEx)
                        {
                            _logger.LogError(queueEx, "Could not enqueue next workflow {WorkflowId}", nextWorkflow.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow run failed");
            run.Status = "Failed";
            run.Error = ex.Message;
            run.CompletedAt = DateTimeOffset.UtcNow;
            try { await db.SaveChangesAsync(cancellationToken); } catch { }
        }
    }

    private async Task<AiModelDefinition> ResolveModelAsync(AppDbContext db, WorkflowDefinition workflow, IConfiguration configuration, CancellationToken ct)
    {
        if (workflow.ModelId.HasValue)
        {
            var m = await db.AiModels.FirstOrDefaultAsync(x => x.Id == workflow.ModelId.Value && x.IsActive, ct);
            if (m != null) return m;
        }
        return new AiModelDefinition
        {
            Name = "Default",
            Provider = "Ollama",
            Endpoint = configuration["Ollama:Endpoint"] ?? "http://localhost:11434",
            ModelName = configuration["Ollama:Model"] ?? "beam:cloud"
        };
    }

    private static string Sanitize(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray());
}
