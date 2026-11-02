using BuildWorkflow.AICenter;
using BuildWorkflow.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api")]
public class ChatApiController : ControllerBase
{
    private readonly AICenterService _aiCenter;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public ChatApiController(AICenterService aiCenter, AppDbContext db, IConfiguration configuration)
    {
        _aiCenter = aiCenter;
        _db = db;
        _configuration = configuration;
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

        try
        {
            var result = await _aiCenter.SendMessageAsync(userId, request.ModelId, request.Message, request.AttachedFileIds, request.SessionId, request.RagBucketId, request.SelectedToolIds, request.SelectedRagBucketIds, cancellationToken);
            return Ok(new { reply = result.Reply, sessionId = result.SessionId, modelName = result.ModelName });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
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

    private Guid? ParseUserId()
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdValue, out var parsedUserId) ? parsedUserId : null;
    }

    public record ChatRequest(string Message, Guid? ModelId = null, Guid[]? AttachedFileIds = null, Guid? SessionId = null, Guid? RagBucketId = null, Guid[]? SelectedToolIds = null, Guid[]? SelectedRagBucketIds = null);
}

