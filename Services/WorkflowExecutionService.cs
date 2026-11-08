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
        var qdrant = scope.ServiceProvider.GetRequiredService<QdrantRagStore>();

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

        var steps = await db.WorkflowSteps
            .Where(s => s.WorkflowId == workflow.Id && s.IsActive)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        if (steps.Count == 0)
        {
            run.Status = "Failed";
            run.Error = "Workflow has no steps configured.";
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
            var aiModelDefault = await ResolveModelAsync(db, workflow, configuration, cancellationToken);
            var allAllowed = await toolExecutor.GetAllowedToolsAsync(run.UserId, null, cancellationToken);

            var session = new WorkflowSession { WorkflowId = workflow.Id, UserId = run.UserId, Title = workflow.Name };
            db.WorkflowSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);
            run.WorkflowSessionId = session.Id;

            var history = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(workflow.SystemInstruction))
                history.Add(new ChatMessage(ChatRole.System, workflow.SystemInstruction));

            var contextSummary = string.Empty;
            string? finalReply = null;
            var generatedFileIds = new HashSet<Guid>();

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var runStep = new WorkflowRunStep
                {
                    WorkflowRunId = run.Id,
                    WorkflowStepId = step.Id,
                    Order = index + 1,
                    Status = "Running",
                    StartedAt = DateTimeOffset.UtcNow
                };
                db.WorkflowRunSteps.Add(runStep);
                await db.SaveChangesAsync(cancellationToken);

                BuildWorkflow.AICenter.Tools.DatabaseToolExecutor.BeginGeneratedFileTracking();

                try
                {
                    var stepModel = step.ModelId.HasValue
                        ? await db.AiModels.FirstOrDefaultAsync(m => m.Id == step.ModelId.Value && m.IsActive, cancellationToken) ?? aiModelDefault
                        : aiModelDefault;

                    var toolIds = (await db.WorkflowStepTools.Where(x => x.WorkflowStepId == step.Id).Select(x => x.ToolId).ToListAsync(cancellationToken)).ToHashSet();
                    var toolDefs = await db.Tools.Include(t => t.ApiConfiguration).Include(t => t.SqlConfiguration).Include(t => t.SftpConfiguration)
                        .Where(t => toolIds.Contains(t.Id)).ToListAsync(cancellationToken);

                    var selectedToolNames = toolDefs.Select(t => Sanitize(t.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var tools = allAllowed.Where(t => selectedToolNames.Contains(Sanitize(t.Name ?? string.Empty))).ToList();

                    var fileIds = await db.WorkflowStepFiles.Where(f => f.WorkflowStepId == step.Id).Select(f => f.UploadedFileId).ToListAsync(cancellationToken);
                    var allowedFileIds = fileIds.Concat(generatedFileIds).Distinct().ToList();
                    var hasGeneratedFiles = generatedFileIds.Count > 0 && allowedFileIds.Any(id => generatedFileIds.Contains(id));

                    if (run.UserId.HasValue && allowedFileIds.Count > 0)
                    {
                        tools = tools.ToList();
                        var attached = toolExecutor.CreateAttachedFileTool(run.UserId.Value, allowedFileIds);
                        attached = toolExecutor.WrapTool(attached, run.UserId.Value, "read_attached_file", "FileReader");
                        tools.Add(attached);
                    }

                    var userMessage = step.UserInstruction ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(contextSummary))
                    {
                        userMessage = $"Context from previous steps:\n{contextSummary}\n\nCurrent step instruction:\n{userMessage}";
                    }

                    if (hasGeneratedFiles)
                    {
                        userMessage += "\n\nGenerated files from previous steps are available. To read them, call read_attached_file with one of these tokens: " +
                            string.Join(", ", generatedFileIds.Select(id => "file:" + id)) + ".";
                    }

                    if (fileIds.Count > 0)
                    {
                        var names = await db.UploadedFiles.Where(f => fileIds.Contains(f.Id)).Select(f => f.OriginalFileName).ToListAsync(cancellationToken);
                        if (names.Count > 0)
                            userMessage = (string.IsNullOrWhiteSpace(userMessage) ? "" : userMessage + "\n\n") + "Attached files: " + string.Join(", ", names);
                        userMessage += "\n\nAttached file tokens for this step only: " + string.Join(", ", fileIds.Select(id => "file:" + id)) + ". Use read_attached_file with one of those tokens if needed.";
                    }

                    var ragBucketIds = new List<Guid>();
                    if (step.RagBucketId.HasValue) ragBucketIds.Add(step.RagBucketId.Value);
                    ragBucketIds.AddRange(await db.WorkflowStepRagBuckets.Where(b => b.WorkflowStepId == step.Id).Select(b => b.RagBucketId).ToListAsync(cancellationToken));
                    ragBucketIds = ragBucketIds.Where(id => id != Guid.Empty).Distinct().ToList();

                    if (ragBucketIds.Count == 0)
                    {
                        if (run.UserId.HasValue)
                        {
                            ragBucketIds = await db.UserRagBuckets.Where(x => x.UserId == run.UserId.Value).Select(x => x.RagBucketId).ToListAsync(cancellationToken);
                            if (ragBucketIds.Count == 0)
                                ragBucketIds = await db.RagBuckets.Where(x => x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
                        }
                        else
                            ragBucketIds = await db.RagBuckets.Where(x => x.IsActive).Select(x => x.Id).ToListAsync(cancellationToken);
                    }

                    var endpoint = configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
                    string? embeddingModel = null;
                    var embeddingBucket = await db.RagBuckets.FirstOrDefaultAsync(b => ragBucketIds.Contains(b.Id) && b.EmbeddingModelId.HasValue, cancellationToken);
                    if (embeddingBucket?.EmbeddingModelId.HasValue == true)
                    {
                        var aiModel = await db.AiModels.FirstOrDefaultAsync(m => m.Id == embeddingBucket.EmbeddingModelId.Value, cancellationToken);
                        if (aiModel != null)
                        {
                            endpoint = string.IsNullOrWhiteSpace(aiModel.Endpoint) ? endpoint : aiModel.Endpoint;
                            embeddingModel = aiModel.ModelName;
                        }
                    }
                    else
                    {
                        embeddingModel = configuration["Ollama:EmbeddingModel"];
                    }
                    var queryEmbedding = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, step.UserInstruction ?? string.Empty, cancellationToken);
                    var selectedContents = await qdrant.SearchAsync(ragBucketIds, step.UserInstruction ?? string.Empty, queryEmbedding, 1000, cancellationToken);
                    var ragContext = BuildWorkflow.RAGPipeline.RagContextBuilder.Build(step.UserInstruction ?? string.Empty, selectedContents);
                    if (!string.IsNullOrWhiteSpace(ragContext))
                    {
                        var modelMessage = "Use the following knowledge to answer the user.\nKnowledge:\n" + ragContext + "\n\nQuestion: " + userMessage;
                        history.Add(new ChatMessage(ChatRole.User, modelMessage));
                    }
                    else
                    {
                        history.Add(new ChatMessage(ChatRole.User, userMessage));
                    }

                    db.WorkflowMessages.Add(new WorkflowMessage { WorkflowSessionId = session.Id, WorkflowRunStepId = runStep.Id, Role = "user", Content = userMessage });

                    var connector = _factory.GetConnector(stepModel);
                    var generatedFileTokens = new List<string>();
                    string reply;
                    try
                    {
                        reply = await connector.ChatAsync(stepModel, history, tools, cancellationToken);
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

                    db.WorkflowMessages.Add(new WorkflowMessage { WorkflowSessionId = session.Id, WorkflowRunStepId = runStep.Id, Role = "assistant", Content = reply });

                    runStep.Status = "Completed";
                    runStep.OutputSummary = reply?.Length > 4000 ? reply.Substring(0, 4000) + "... [truncated]" : reply;
                    runStep.CompletedAt = DateTimeOffset.UtcNow;
                    runStep.WorkflowSessionId = session.Id;
                    await db.SaveChangesAsync(cancellationToken);

                    foreach (var token in generatedFileTokens)
                    {
                        var guidPart = token.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                            ? token.Substring("file:".Length)
                            : token;
                        if (Guid.TryParse(guidPart, out var fid)) generatedFileIds.Add(fid);
                    }

                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(reply ?? string.Empty, @"file:[0-9a-fA-F\-]+"))
                    {
                        var token = m.Value;
                        var guidPart = token.Replace("file:", string.Empty);
                        if (Guid.TryParse(guidPart, out var fid)) generatedFileIds.Add(fid);
                    }

                    var generatedTokenList = string.Join(", ", generatedFileIds.Select(id => "file:" + id));
                    var stepDigest = $"Step {index + 1} ({step.Name}): " + (reply?.Length > 1200 ? reply.Substring(0, 1200) + "... [truncated]" : reply);
                    if (!string.IsNullOrWhiteSpace(generatedTokenList))
                        stepDigest += "\nGenerated file tokens: " + generatedTokenList;
                    contextSummary = string.IsNullOrWhiteSpace(contextSummary)
                        ? stepDigest
                        : (contextSummary + "\n\n" + stepDigest);
                    if (contextSummary.Length > 8000)
                    {
                        contextSummary = contextSummary.Substring(contextSummary.Length - 8000);
                    }

                    finalReply = reply;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow step failed");
                    runStep.Status = "Failed";
                    runStep.Error = ex.Message;
                    runStep.CompletedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    throw;
                }
            }

            run.Status = "Completed";
            run.OutputSummary = finalReply?.Length > 4000 ? finalReply.Substring(0, 4000) + "... [truncated]" : finalReply;
            run.ContextSummary = contextSummary;
            run.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
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