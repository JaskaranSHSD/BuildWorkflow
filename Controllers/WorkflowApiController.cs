using BuildWorkflow.AICenter;
using BuildWorkflow.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api/workflows")]
[Authorize]
public class WorkflowApiController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AICenterService _ai;

    public WorkflowApiController(AppDbContext db, AICenterService ai)
    {
        _db = db;
        _ai = ai;
    }

    [HttpPost("from-session")]
    public async Task<IActionResult> CreateFromSession([FromBody] CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.SessionId == Guid.Empty || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "SessionId and Name are required." });
        }

        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var session = await _db.ChatSessions
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == request.SessionId && s.UserId == userId, cancellationToken);

        if (session == null) return NotFound();

        var userMessages = session.Messages
            .Where(m => m.Role == "user")
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.Content)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        if (userMessages.Count == 0) return BadRequest(new { error = "The chat has no user messages to convert." });

        var workflow = new Models.ChatWorkflow
        {
            UserId = userId,
            Name = request.Name.Trim(),
            RagBucketId = request.RagBucketId
        };
        _db.ChatWorkflows.Add(workflow);
        await _db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < userMessages.Count; i++)
        {
            _db.ChatWorkflowSteps.Add(new Models.ChatWorkflowStep
            {
                WorkflowId = workflow.Id,
                StepOrder = i + 1,
                UserMessage = userMessages[i]
            });
        }
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { id = workflow.Id, name = workflow.Name, stepCount = userMessages.Count });
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var workflows = await _db.ChatWorkflows
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                id = w.Id,
                name = w.Name,
                createdAt = w.CreatedAt,
                stepCount = w.Steps.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(workflows);
    }

    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id, [FromBody] RunWorkflowRequest request, CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var workflow = await _db.ChatWorkflows
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, cancellationToken);

        if (workflow == null) return NotFound();

        var workflowFileIds = await _db.ChatWorkflowFiles
            .Where(f => f.WorkflowId == workflow.Id)
            .Select(f => f.UploadedFileId)
            .ToListAsync(cancellationToken);

        Guid? sessionId = null;
        var results = new List<object>();

        foreach (var step in workflow.Steps.OrderBy(s => s.StepOrder))
        {
            var result = await _ai.SendMessageAsync(userId, request?.ModelId, step.UserMessage, workflowFileIds, sessionId, workflow.RagBucketId, null, null, cancellationToken);
            sessionId = result.SessionId;
            results.Add(new { step = step.StepOrder, prompt = step.UserMessage, reply = result.Reply });
        }

        return Ok(new { workflowId = workflow.Id, sessionId, results });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var workflow = await _db.ChatWorkflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, cancellationToken);
        if (workflow == null) return NotFound();

        _db.ChatWorkflows.Remove(workflow);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/files")]
    public async Task<IActionResult> ListFiles(Guid id, CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var exists = await _db.ChatWorkflows.AnyAsync(w => w.Id == id && w.UserId == userId, cancellationToken);
        if (!exists) return NotFound();

        var files = await _db.ChatWorkflowFiles
            .Where(f => f.WorkflowId == id)
            .Join(_db.UploadedFiles, wf => wf.UploadedFileId, uf => uf.Id, (wf, uf) => new { id = uf.Id, name = uf.OriginalFileName, size = uf.Size })
            .ToListAsync(cancellationToken);

        return Ok(files);
    }

    [HttpPost("{id:guid}/files")]
    public async Task<IActionResult> AttachFile(Guid id, [FromBody] AttachWorkflowFileRequest request, CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var exists = await _db.ChatWorkflows.AnyAsync(w => w.Id == id && w.UserId == userId, cancellationToken);
        if (!exists) return NotFound();

        if (request == null || request.FileId == Guid.Empty) return BadRequest();

        var ownsFile = await _db.UploadedFiles.AnyAsync(f => f.Id == request.FileId && (f.UserId == userId || f.UserId == null), cancellationToken);
        if (!ownsFile) return Forbid();

        var already = await _db.ChatWorkflowFiles.AnyAsync(f => f.WorkflowId == id && f.UploadedFileId == request.FileId, cancellationToken);
        if (!already)
        {
            _db.ChatWorkflowFiles.Add(new Models.ChatWorkflowFile
            {
                WorkflowId = id,
                UploadedFileId = request.FileId
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    [HttpDelete("{id:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> DetachFile(Guid id, Guid fileId, CancellationToken cancellationToken)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var exists = await _db.ChatWorkflows.AnyAsync(w => w.Id == id && w.UserId == userId, cancellationToken);
        if (!exists) return NotFound();

        var link = await _db.ChatWorkflowFiles.FirstOrDefaultAsync(f => f.WorkflowId == id && f.UploadedFileId == fileId, cancellationToken);
        if (link != null)
        {
            _db.ChatWorkflowFiles.Remove(link);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private Guid? ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public record CreateWorkflowRequest(Guid SessionId, string Name, Guid? RagBucketId = null);
    public record RunWorkflowRequest(Guid? ModelId = null);
    public record AttachWorkflowFileRequest(Guid FileId);
}
