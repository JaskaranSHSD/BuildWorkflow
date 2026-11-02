using BuildWorkflow.Data;
using BuildWorkflow.Models;
using BuildWorkflow.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[ApiController]
[Route("api/workflow")]
[Authorize]
public class WorkflowModuleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly WorkflowRunQueue _queue;

    public WorkflowModuleController(AppDbContext db, WorkflowRunQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var workflows = await _db.Workflows
            .Where(w => w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == w.Id && s.UserId == userId.Value))
            .OrderByDescending(w => w.UpdatedAt)
            .ToListAsync(ct);
        var result = new List<object>();
        foreach (var w in workflows)
        {
            var fileNames = await _db.WorkflowFiles
                .Where(f => f.WorkflowId == w.Id)
                .Join(_db.UploadedFiles, wf => wf.UploadedFileId, uf => uf.Id, (wf, uf) => uf.OriginalFileName)
                .ToListAsync(ct);
            result.Add(new
            {
                id = w.Id,
                name = w.Name,
                isDependent = workflows.Any(x => x.NextWorkflowId == w.Id),
                userInstruction = w.UserInstruction,
                systemInstruction = w.SystemInstruction,
                modelId = w.ModelId,
                nextWorkflowId = w.NextWorkflowId,
                nextWorkflowName = w.NextWorkflowId.HasValue ? _db.Workflows.Where(x => x.Id == w.NextWorkflowId.Value).Select(x => x.Name).FirstOrDefault() : null,
                ragBucketId = w.RagBucketId,
                toolCount = _db.WorkflowTools.Count(t => t.WorkflowId == w.Id),
                fileCount = fileNames.Count,
                fileNames,
                runCount = _db.WorkflowRuns.Count(r => r.WorkflowId == w.Id),
                isOwner = w.UserId == userId,
                updatedAt = w.UpdatedAt
            });
        }
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { error = "Name required." });
        var wf = new WorkflowDefinition
        {
            UserId = userId,
            Name = req.Name.Trim(),
            SystemInstruction = req.SystemInstruction,
            UserInstruction = req.UserInstruction ?? string.Empty,
            ModelId = req.ModelId,
            RagBucketId = req.RagBucketId,
            NextWorkflowId = req.NextWorkflowId
        };
        _db.Workflows.Add(wf);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = wf.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var wf = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId.Value)), ct);
        if (wf == null) return NotFound();
        return Ok(new
        {
            id = wf.Id,
            name = wf.Name,
            systemInstruction = wf.SystemInstruction,
            userInstruction = wf.UserInstruction,
            modelId = wf.ModelId,
            modelName = wf.ModelId.HasValue ? _db.AiModels.Where(m => m.Id == wf.ModelId.Value).Select(m => m.Name).FirstOrDefault() : null,
            nextWorkflowId = wf.NextWorkflowId,
            nextWorkflowName = wf.NextWorkflowId.HasValue ? _db.Workflows.Where(w => w.Id == wf.NextWorkflowId.Value).Select(w => w.Name).FirstOrDefault() : null,
            ragBucketId = wf.RagBucketId,
            tools = _db.WorkflowTools.Where(t => t.WorkflowId == id).Select(t => t.ToolId),
            ragBuckets = _db.WorkflowRagBuckets.Where(b => b.WorkflowId == id).Select(b => b.RagBucketId),
            files = _db.WorkflowFiles.Where(f => f.WorkflowId == id).Select(f => f.UploadedFileId),
            fileNames = await _db.WorkflowFiles.Where(f => f.WorkflowId == id)
                .Join(_db.UploadedFiles, wfFile => wfFile.UploadedFileId, uploadedFile => uploadedFile.Id, (wfFile, uploadedFile) => uploadedFile.OriginalFileName)
                .ToListAsync(ct)
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var wf = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct);
        if (wf == null) return NotFound();
        if (!string.IsNullOrWhiteSpace(req.Name)) wf.Name = req.Name.Trim();
        wf.SystemInstruction = req.SystemInstruction;
        wf.UserInstruction = req.UserInstruction ?? wf.UserInstruction;
        wf.ModelId = req.ModelId;
        wf.RagBucketId = req.RagBucketId;
        wf.NextWorkflowId = req.NextWorkflowId;
        if (req.ToolIds != null)
        {
            var existingTools = await _db.WorkflowTools.Where(t => t.WorkflowId == id).ToListAsync(ct);
            _db.WorkflowTools.RemoveRange(existingTools);
            foreach (var tid in req.ToolIds.Distinct())
                _db.WorkflowTools.Add(new WorkflowTool { WorkflowId = id, ToolId = tid });
        }
        if (req.RagBucketIds != null)
        {
            var existingBuckets = await _db.WorkflowRagBuckets.Where(b => b.WorkflowId == id).ToListAsync(ct);
            _db.WorkflowRagBuckets.RemoveRange(existingBuckets);
            foreach (var bid in req.RagBucketIds.Distinct())
                _db.WorkflowRagBuckets.Add(new WorkflowRagBucket { WorkflowId = id, RagBucketId = bid });
        }
        if (req.FileIds != null)
        {
            var existingFiles = await _db.WorkflowFiles.Where(f => f.WorkflowId == id).ToListAsync(ct);
            _db.WorkflowFiles.RemoveRange(existingFiles);
            foreach (var fileId in req.FileIds.Distinct())
            {
                var ownsFile = await _db.UploadedFiles.AnyAsync(f => f.Id == fileId && (f.UserId == userId || f.UserId == null), ct);
                if (ownsFile) _db.WorkflowFiles.Add(new WorkflowFile { WorkflowId = id, UploadedFileId = fileId });
            }
        }
        wf.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var wf = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct);
        if (wf == null) return NotFound();
        _db.Workflows.Remove(wf);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/tools")]
    public async Task<IActionResult> SetTools(Guid id, [FromBody] Guid[] toolIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var wf = await _db.Workflows.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId, ct);
        if (wf == null) return NotFound();
        var existingTools = await _db.WorkflowTools.Where(t => t.WorkflowId == id).ToListAsync(ct);
        _db.WorkflowTools.RemoveRange(existingTools);
        foreach (var tid in toolIds.Distinct())
            _db.WorkflowTools.Add(new WorkflowTool { WorkflowId = id, ToolId = tid });
        wf.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/files")]
    public async Task<IActionResult> AttachFile(Guid id, [FromBody] Guid fileId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var exists = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!exists) return NotFound();
        var ownsFile = await _db.UploadedFiles.AnyAsync(f => f.Id == fileId && (f.UserId == userId || f.UserId == null), ct);
        if (!ownsFile) return Forbid();
        var already = await _db.WorkflowFiles.AnyAsync(f => f.WorkflowId == id && f.UploadedFileId == fileId, ct);
        if (!already) _db.WorkflowFiles.Add(new WorkflowFile { WorkflowId = id, UploadedFileId = fileId });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/ragbuckets")]
    public async Task<IActionResult> SetRagBuckets(Guid id, [FromBody] Guid[] bucketIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var exists = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!exists) return NotFound();
        var existing = await _db.WorkflowRagBuckets.Where(b => b.WorkflowId == id).ToListAsync(ct);
        _db.WorkflowRagBuckets.RemoveRange(existing);
        foreach (var bid in bucketIds.Distinct())
            _db.WorkflowRagBuckets.Add(new WorkflowRagBucket { WorkflowId = id, RagBucketId = bid });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/run")]
    public async Task<IActionResult> Run(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var exists = await _db.Workflows.AnyAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId.Value)), ct);
        if (!exists) return NotFound();

        var isDependentChild = await _db.Workflows.AnyAsync(w => w.NextWorkflowId == id, ct);
        if (isDependentChild)
        {
            return BadRequest(new { error = "This is a dependent workflow and cannot be run directly. Run its parent workflow instead." });
        }

        var run = new WorkflowRun { WorkflowId = id, UserId = userId, Status = "Queued" };
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _queue.Enqueue(run.Id);
        return Ok(new { runId = run.Id });
    }

    [HttpGet("{id:guid}/runs")]
    public async Task<IActionResult> Runs(Guid id, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var hasAccess = await _db.Workflows.AnyAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId.Value)), ct);
        if (!hasAccess) return NotFound();

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _db.WorkflowRuns.Where(r => r.WorkflowId == id).OrderByDescending(r => r.CreatedAt);
        var totalCount = await query.CountAsync(ct);
        var runs = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.Status, r.CreatedAt, r.StartedAt, r.CompletedAt, r.Error, r.ModelId, sessionId = r.WorkflowSessionId, parentRunId = r.ParentRunId, Page = page, PageSize = pageSize, TotalCount = totalCount })
            .ToListAsync(ct);
        Response.Headers["X-Total-Count"] = totalCount.ToString();
        return Ok(runs);
    }

    [HttpGet("runs/{runId:guid}/messages")]
    public async Task<IActionResult> RunMessages(Guid runId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var run = await _db.WorkflowRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run == null) return NotFound();
        if (run.WorkflowId != Guid.Empty)
        {
            var hasAccess = await _db.Workflows.AnyAsync(w => w.Id == run.WorkflowId && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == run.WorkflowId && s.UserId == userId.Value)), ct);
            if (!hasAccess) return NotFound();
        }
        if (run.WorkflowSessionId == null) return Ok(Array.Empty<object>());
        var msgs = await _db.WorkflowMessages.Where(m => m.WorkflowSessionId == run.WorkflowSessionId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Role, m.Content, m.CreatedAt })
            .ToListAsync(ct);
        return Ok(msgs);
    }

    [HttpPost("{id:guid}/schedule")]
    public async Task<IActionResult> Schedule(Guid id, [FromBody] ScheduleRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var exists = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!exists) return NotFound();
        var existing = await _db.WorkflowSchedules.Where(s => s.WorkflowId == id).ToListAsync(ct);
        foreach (var old in existing) old.Enabled = false;
        var schedule = new WorkflowSchedule
        {
            WorkflowId = id,
            UserId = userId,
            Enabled = true,
            CronExpression = req.CronExpression,
            IntervalMinutes = req.IntervalMinutes,
            RunAt = req.RunAt,
            NextRunAt = req.RunAt ?? (req.IntervalMinutes.HasValue ? DateTimeOffset.UtcNow.AddMinutes(req.IntervalMinutes.Value) : DateTimeOffset.UtcNow)
        };
        _db.WorkflowSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        return Ok(new { scheduleId = schedule.Id });
    }

    [HttpGet("{id:guid}/schedules")]
    public async Task<IActionResult> Schedules(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var list = await _db.WorkflowSchedules.Where(s => s.WorkflowId == id)
            .Select(s => new { s.Id, s.Enabled, s.IntervalMinutes, s.RunAt, s.NextRunAt, s.LastRunAt })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("/api/users")]
    public async Task<IActionResult> SharingUsers(CancellationToken ct)
    {
        var current = ParseUserId();
        if (!current.HasValue) return Unauthorized();
        var users = await _db.Users
            .Where(u => u.Id != current.Value)
            .OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, FullName = u.FullName ?? u.Username })
            .ToListAsync(ct);
        return Ok(users);
    }

    [HttpGet("{id:guid}/shares")]
    public async Task<IActionResult> ListShares(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var shares = await _db.WorkflowShares
            .Where(s => s.WorkflowId == id)
            .Join(_db.Users, s => s.UserId, u => u.Id, (s, u) => new { s.Id, s.UserId, UserName = u.Username, FullName = u.FullName ?? u.Username, s.Permission })
            .ToListAsync(ct);
        return Ok(shares);
    }

    [HttpPost("{id:guid}/shares")]
    public async Task<IActionResult> ShareWorkflow(Guid id, [FromBody] ShareRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();

        var isDependentChild = await _db.Workflows.AnyAsync(w => w.NextWorkflowId == id, ct);
        if (isDependentChild) return BadRequest(new { error = "Dependent workflows cannot be shared independently. Share the parent workflow instead." });

        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        if (!await _db.Users.AnyAsync(u => u.Id == req.UserId, ct)) return NotFound();
        var exists = await _db.WorkflowShares.AnyAsync(s => s.WorkflowId == id && s.UserId == req.UserId, ct);
        if (!exists)
        {
            _db.WorkflowShares.Add(new WorkflowShare { WorkflowId = id, UserId = req.UserId, Permission = "Run" });
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpDelete("{id:guid}/shares/{shareId:guid}")]
    public async Task<IActionResult> RevokeShare(Guid id, Guid shareId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var share = await _db.WorkflowShares.FirstOrDefaultAsync(s => s.Id == shareId && s.WorkflowId == id, ct);
        if (share != null)
        {
            _db.WorkflowShares.Remove(share);
            await _db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    public record ShareRequest(Guid UserId);

    [HttpDelete("schedules/{scheduleId:guid}")]
    public async Task<IActionResult> DeleteSchedule(Guid scheduleId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var schedule = await _db.WorkflowSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule == null) return NotFound();
        var owns = await _db.Workflows.AnyAsync(w => w.Id == schedule.WorkflowId && w.UserId == userId, ct);
        if (!owns) return Forbid();
        _db.WorkflowSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private Guid? ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public record CreateRequest(string Name, string? SystemInstruction, string? UserInstruction, Guid? ModelId, Guid? RagBucketId, Guid? NextWorkflowId = null);
    public record UpdateRequest(string? Name, string? SystemInstruction, string? UserInstruction, Guid? ModelId, Guid? RagBucketId, Guid? NextWorkflowId = null, Guid[]? ToolIds = null, Guid[]? RagBucketIds = null, Guid[]? FileIds = null);
    public record ScheduleRequest(string? CronExpression, int? IntervalMinutes, DateTimeOffset? RunAt);
}
