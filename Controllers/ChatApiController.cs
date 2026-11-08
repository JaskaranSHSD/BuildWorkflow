using BuildWorkflow.AICenter;
using BuildWorkflow.Data;
using BuildWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api")]
public class ChatApiController : ControllerBase
{
    public record KnowledgeSearchRequest(string Query, Guid[]? BucketIds);
    private readonly AICenterService _aiCenter;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly QdrantRagStore _qdrant;

    public ChatApiController(AICenterService aiCenter, AppDbContext db, IConfiguration configuration, QdrantRagStore qdrant)
    {
        _aiCenter = aiCenter;
        _db = db;
        _configuration = configuration;
        _qdrant = qdrant;
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message) && (request.AttachedFileIds == null || request.AttachedFileIds.Length == 0))
        {
            return BadRequest(new { error = "Message is required." });
        }

        Guid? userId = null;
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdValue, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        if (request.AttachedFileIds != null && request.AttachedFileIds.Length > 0)
        {
            var validationResult = await ValidateAttachmentLimitsAsync(request.AttachedFileIds, cancellationToken);
            if (validationResult != null)
            {
                return validationResult;
            }
        }

        try
        {
            var result = await _aiCenter.SendMessageAsync(userId, request.ModelId, request.Message, request.AttachedFileIds, request.SessionId, request.RagBucketId, request.SelectedToolIds, request.SelectedRagBucketIds, CancellationToken.None);
            return Ok(new { reply = result.Reply, sessionId = result.SessionId, modelName = result.ModelName });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("knowledge/search")]
    public async Task<IActionResult> SearchKnowledge([FromBody] KnowledgeSearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Query is required." });

        Guid? userId = null;
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdValue, out var parsedUserId)) userId = parsedUserId;

        var isAdmin = User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin");
        var available = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).Select(b => b.Id).ToListAsync(cancellationToken)
            : (userId.HasValue
                ? await _db.UserRagBuckets.Where(a => a.UserId == userId.Value).Select(a => a.RagBucketId).ToListAsync(cancellationToken)
                : await _db.RagBuckets.Where(b => b.IsActive).Select(b => b.Id).ToListAsync(cancellationToken));
        if (!isAdmin && userId.HasValue && available.Count == 0)
        {
            available = await _db.RagBuckets.Where(b => b.IsActive).Select(b => b.Id).ToListAsync(cancellationToken);
        }

        var requested = (request.BucketIds ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        var bucketIds = requested.Count > 0 ? requested.Intersect(available).ToList() : available;
        if (bucketIds.Count == 0) return Ok(Array.Empty<object>());

        var endpoint = _configuration["Ollama:Endpoint"] ?? "http://localhost:11434";
        var embeddingModel = _configuration["Ollama:EmbeddingModel"];
        var queryEmbedding = await BuildWorkflow.RAGPipeline.RagEmbeddingGenerator.GenerateAsync(endpoint, embeddingModel, request.Query, cancellationToken);

        var hits = await _qdrant.SearchHitsAsync(bucketIds, request.Query, queryEmbedding, 25, cancellationToken);
        var result = hits.Select(h => new
        {
            score = h.Score,
            bucketId = h.BucketId,
            documentId = h.DocumentId,
            content = h.Content.Length > 1200 ? h.Content.Substring(0, 1200) + "..." : h.Content
        });
        return Ok(result);
    }

    [HttpPost("chat/stream")]
    public async Task ChatStream([FromBody] ChatRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Message) && (request.AttachedFileIds == null || request.AttachedFileIds.Length == 0))
        {
            Response.StatusCode = 400;
            await Response.WriteAsJsonAsync(new { error = "Message is required." }, cancellationToken);
            return;
        }

        Guid? userId = null;
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdValue, out var parsedUserId))
        {
            userId = parsedUserId;
        }

        if (request.AttachedFileIds != null && request.AttachedFileIds.Length > 0)
        {
            var validationResult = await ValidateAttachmentLimitsAsync(request.AttachedFileIds, cancellationToken);
            if (validationResult != null)
            {
                await validationResult.ExecuteResultAsync(ControllerContext);
                return;
            }
        }

        Response.Headers["Content-Type"] = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";

        try
        {
            await foreach (var chunk in _aiCenter.SendMessageStreamAsync(userId, request.ModelId, request.Message, request.AttachedFileIds, request.SessionId, request.RagBucketId, request.SelectedToolIds, request.SelectedRagBucketIds, CancellationToken.None))
            {
                try
                {
                    await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(chunk)}\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                }
                catch
                {
                    // Client may have left; continue processing and persist the reply.
                }
            }
        }
        catch (ArgumentException ex)
        {
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new AiChatStreamChunk(null, null, null, true, ex.Message))}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await Response.WriteAsync($"data: {System.Text.Json.JsonSerializer.Serialize(new AiChatStreamChunk(null, null, null, true, ex.Message))}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    [Authorize]
    [HttpGet("chat/sessions")]
    public async Task<IActionResult> GetSessions(int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        Guid? userId = ParseUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _db.ChatSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.UpdatedAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var sessions = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new { id = s.Id, title = s.Title, updatedAt = s.UpdatedAt, createdAt = s.CreatedAt, page, pageSize, totalCount })
            .ToListAsync(cancellationToken);

        Response.Headers["X-Total-Count"] = totalCount.ToString();
        return Ok(sessions);
    }

    [Authorize]
    [HttpGet("chat/sessions/{id:guid}/messages")]
    public async Task<IActionResult> GetSessionMessages(Guid id, CancellationToken cancellationToken)
    {
        Guid? userId = ParseUserId();
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);

        if (session == null)
        {
            return NotFound();
        }

        var maxMessages = _configuration is null ? 50 : (_configuration.GetValue<int?>("Chat:MaxMessagesPerSession") ?? 50);
        if (session.Messages.Count > maxMessages)
        {
            return BadRequest(new { error = $"This chat has reached the maximum of {maxMessages} messages. Please start a new chat." });
        }

        var messages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { id = m.Id, role = m.Role, content = m.Content, attachedFileIds = m.AttachedFileIds, createdAt = m.CreatedAt })
            .ToList();

        return Ok(messages);
    }

    private async Task<IActionResult?> ValidateAttachmentLimitsAsync(Guid[] attachedFileIds, CancellationToken cancellationToken)
    {
        var uploadedFiles = await _db.UploadedFiles
            .AsNoTracking()
            .Where(f => attachedFileIds.Contains(f.Id))
            .Select(f => f.ContentType)
            .ToListAsync(cancellationToken);

        var imageCount = uploadedFiles.Count(ct => !string.IsNullOrWhiteSpace(ct) && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
        var fileCount = uploadedFiles.Count - imageCount;

        if (imageCount > 2 || fileCount > 2)
        {
            return BadRequest(new { error = "You can attach at most 2 files and 2 images per chat message." });
        }

        return null;
    }

    private Guid? ParseUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;
    }

    public record ChatRequest(string Message, Guid? ModelId = null, Guid[]? AttachedFileIds = null, Guid? SessionId = null, Guid? RagBucketId = null, Guid[]? SelectedToolIds = null, Guid[]? SelectedRagBucketIds = null);
}

