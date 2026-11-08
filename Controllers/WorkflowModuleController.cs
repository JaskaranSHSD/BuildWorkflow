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
    public async Task<IActionResult> List(int page = 1, int pageSize = 10, string? search = null, CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize < 1 || pageSize > 100 ? 10 : pageSize;
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var query = _db.Workflows
            .Where(w => w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == w.Id && s.UserId == userId.Value));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(w => (w.Name ?? "").ToLower().Contains(term) || (w.Description ?? "").ToLower().Contains(term));
        }
        var baseQuery = query.OrderByDescending(w => w.UpdatedAt);
        var totalCount = await baseQuery.CountAsync(ct);
        var workflows = await baseQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        Response.Headers["X-Total-Count"] = totalCount.ToString();
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
                description = w.Description,
                userInstruction = w.UserInstruction,
                systemInstruction = w.SystemInstruction,
                modelId = w.ModelId,
                ragBucketId = w.RagBucketId,
                stepCount = _db.WorkflowSteps.Count(st => st.WorkflowId == w.Id),
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
            Description = req.Description,
            SystemInstruction = req.SystemInstruction,
            UserInstruction = req.UserInstruction ?? string.Empty,
            ModelId = req.ModelId,
            RagBucketId = req.RagBucketId
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
            description = wf.Description,
            systemInstruction = wf.SystemInstruction,
            userInstruction = wf.UserInstruction,
            modelId = wf.ModelId,
            modelName = wf.ModelId.HasValue ? _db.AiModels.Where(m => m.Id == wf.ModelId.Value).Select(m => m.Name).FirstOrDefault() : null,
            ragBucketId = wf.RagBucketId,
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
        if (req.Description != null) wf.Description = req.Description;
        wf.SystemInstruction = req.SystemInstruction;
        wf.UserInstruction = req.UserInstruction ?? wf.UserInstruction;
        wf.ModelId = req.ModelId;
        wf.RagBucketId = req.RagBucketId;
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

        var run = new WorkflowRun { WorkflowId = id, UserId = userId, Status = "Queued" };
        _db.WorkflowRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        _queue.Enqueue(run.Id);
        return Ok(new { runId = run.Id });
    }

    [HttpDelete("{id:guid}/runs")]
    public async Task<IActionResult> ClearRuns(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var owns = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!owns) return NotFound();

        var runIds = await _db.WorkflowRuns.Where(r => r.WorkflowId == id).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count > 0)
        {
            var runSteps = await _db.WorkflowRunSteps.Where(s => runIds.Contains(s.WorkflowRunId)).ToListAsync(ct);
            _db.WorkflowRunSteps.RemoveRange(runSteps);
            var runs = await _db.WorkflowRuns.Where(r => r.WorkflowId == id).ToListAsync(ct);
            _db.WorkflowRuns.RemoveRange(runs);
            await _db.SaveChangesAsync(ct);
        }

        return NoContent();
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
            .Select(r => new { r.Id, r.Status, r.CreatedAt, r.StartedAt, r.CompletedAt, r.Error, r.ModelId, sessionId = r.WorkflowSessionId, Page = page, PageSize = pageSize, TotalCount = totalCount })
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
            .Select(m => new { m.Role, m.Content, m.CreatedAt, stepId = m.WorkflowRunStepId })
            .ToListAsync(ct);
        return Ok(msgs);
    }

    [HttpGet("runs/{runId:guid}/steps")]
    public async Task<IActionResult> RunSteps(Guid runId, CancellationToken ct)
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
        var steps = await _db.WorkflowRunSteps
            .Where(s => s.WorkflowRunId == runId)
            .OrderBy(s => s.Order)
            .Select(s => new
            {
                s.Id,
                s.Order,
                s.Status,
                s.StartedAt,
                s.CompletedAt,
                s.Error,
                stepName = _db.WorkflowSteps.Where(st => st.Id == s.WorkflowStepId).Select(st => st.Name).FirstOrDefault()
            })
            .ToListAsync(ct);
        return Ok(steps);
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

        var runAt = req.RunAt?.ToUniversalTime();
        var hour = req.Hour ?? (runAt.HasValue ? runAt.Value.Hour : (string.Equals(req.ScheduleType, "Interval", StringComparison.OrdinalIgnoreCase) ? (int?)null : 9));
        var minute = req.Minute ?? (runAt.HasValue ? runAt.Value.Minute : (string.Equals(req.ScheduleType, "Interval", StringComparison.OrdinalIgnoreCase) ? (int?)null : 0));

        var schedule = new WorkflowSchedule
        {
            WorkflowId = id,
            UserId = userId,
            Enabled = true,
            ScheduleType = string.IsNullOrWhiteSpace(req.ScheduleType) ? "Once" : req.ScheduleType!,
            CronExpression = req.CronExpression,
            IntervalMinutes = req.IntervalMinutes,
            RunAt = runAt,
            DaysOfWeek = req.DaysOfWeek,
            DayOfMonth = req.DayOfMonth,
            WeekOfMonth = req.WeekOfMonth,
            DayOfWeek = req.DayOfWeek,
            Hour = hour,
            Minute = minute,
            TimeZoneId = req.TimeZoneId,
            EndAt = req.EndAt?.ToUniversalTime()
        };

        // One-off schedules that only have a date/time but no explicit type.
        if (schedule.ScheduleType == "Once" && !runAt.HasValue && req.IntervalMinutes.HasValue)
            schedule.ScheduleType = "Interval";

        schedule.NextRunAt = RecurrenceCalculator.GetNextRun(schedule, DateTimeOffset.UtcNow, true)
            ?? (runAt.HasValue ? runAt.Value : (req.IntervalMinutes.HasValue ? DateTimeOffset.UtcNow.AddMinutes(req.IntervalMinutes.Value) : DateTimeOffset.UtcNow));

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
            .Select(s => new { s.Id, s.Enabled, s.IntervalMinutes, s.RunAt, s.NextRunAt, s.LastRunAt, s.ScheduleType, s.DaysOfWeek, s.DayOfMonth, s.WeekOfMonth, s.DayOfWeek, s.Hour, s.Minute, s.TimeZoneId, s.EndAt })
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

    [HttpPut("schedules/{scheduleId:guid}")]
    public async Task<IActionResult> UpdateSchedule(Guid scheduleId, [FromBody] ScheduleRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var schedule = await _db.WorkflowSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId, ct);
        if (schedule == null) return NotFound();
        var owns = await _db.Workflows.AnyAsync(w => w.Id == schedule.WorkflowId && w.UserId == userId, ct);
        if (!owns) return Forbid();

        schedule.ScheduleType = string.IsNullOrWhiteSpace(req.ScheduleType) ? schedule.ScheduleType : req.ScheduleType!;
        schedule.CronExpression = req.CronExpression;
        if (req.IntervalMinutes.HasValue) schedule.IntervalMinutes = req.IntervalMinutes;
        if (req.RunAt.HasValue) schedule.RunAt = req.RunAt.Value.ToUniversalTime();
        schedule.DaysOfWeek = req.DaysOfWeek;
        if (req.DayOfMonth.HasValue) schedule.DayOfMonth = req.DayOfMonth;
        if (req.WeekOfMonth.HasValue) schedule.WeekOfMonth = req.WeekOfMonth;
        if (req.DayOfWeek.HasValue) schedule.DayOfWeek = req.DayOfWeek;
        if (req.Hour.HasValue) schedule.Hour = req.Hour;
        if (req.Minute.HasValue) schedule.Minute = req.Minute;
        if (!string.IsNullOrWhiteSpace(req.TimeZoneId)) schedule.TimeZoneId = req.TimeZoneId;
        if (req.EndAt.HasValue) schedule.EndAt = req.EndAt.Value.ToUniversalTime();
        schedule.Enabled = true;
        schedule.NextRunAt = RecurrenceCalculator.GetNextRun(schedule, DateTimeOffset.UtcNow, true)
            ?? (schedule.RunAt.HasValue ? schedule.RunAt.Value : DateTimeOffset.UtcNow);

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/steps")]
    public async Task<IActionResult> ListSteps(Guid id, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var hasAccess = await _db.Workflows.AnyAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId.Value)), ct);
        if (!hasAccess) return NotFound();
        var steps = await _db.WorkflowSteps.Where(s => s.WorkflowId == id).OrderBy(s => s.Order).ThenBy(s => s.CreatedAt).ToListAsync(ct);
        var result = new List<object>();
        foreach (var s in steps)
        {
            result.Add(new
            {
                id = s.Id,
                name = s.Name,
                order = s.Order,
                userInstruction = s.UserInstruction,
                modelId = s.ModelId,
                ragBucketId = s.RagBucketId,
                toolIds = await _db.WorkflowStepTools.Where(t => t.WorkflowStepId == s.Id).Select(t => t.ToolId).ToListAsync(ct),
                ragBucketIds = await _db.WorkflowStepRagBuckets.Where(b => b.WorkflowStepId == s.Id).Select(b => b.RagBucketId).ToListAsync(ct),
                fileIds = await _db.WorkflowStepFiles.Where(f => f.WorkflowStepId == s.Id).Select(f => f.UploadedFileId).ToListAsync(ct)
            });
        }
        return Ok(result);
    }

    [HttpPost("{id:guid}/steps")]
    public async Task<IActionResult> CreateStep(Guid id, [FromBody] StepRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var nextOrder = (await _db.WorkflowSteps.Where(s => s.WorkflowId == id).MaxAsync(s => (int?)s.Order, ct) ?? 0) + 1;
        var step = new WorkflowStep
        {
            WorkflowId = id,
            Order = req.Order ?? nextOrder,
            Name = string.IsNullOrWhiteSpace(req.Name) ? $"Step {nextOrder}" : req.Name.Trim(),
            UserInstruction = req.UserInstruction,
            ModelId = req.ModelId,
            RagBucketId = req.RagBucketId
        };
        _db.WorkflowSteps.Add(step);
        await _db.SaveChangesAsync(ct);
        return Ok(new { id = step.Id });
    }

    [HttpPut("steps/{stepId:guid}")]
    public async Task<IActionResult> UpdateStep(Guid stepId, [FromBody] StepRequest req, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        if (!string.IsNullOrWhiteSpace(req.Name)) step.Name = req.Name.Trim();
        step.UserInstruction = req.UserInstruction;
        step.ModelId = req.ModelId;
        step.RagBucketId = req.RagBucketId;
        if (req.Order.HasValue) step.Order = req.Order.Value;
        step.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("steps/{stepId:guid}")]
    public async Task<IActionResult> DeleteStep(Guid stepId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        _db.WorkflowSteps.Remove(step);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/steps/reorder")]
    public async Task<IActionResult> ReorderSteps(Guid id, [FromBody] Guid[] stepIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == id && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        for (var i = 0; i < stepIds.Length; i++)
        {
            var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepIds[i] && s.WorkflowId == id, ct);
            if (step != null) step.Order = i + 1;
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("steps/{stepId:guid}/tools")]
    public async Task<IActionResult> SetStepTools(Guid stepId, [FromBody] Guid[] toolIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var existing = await _db.WorkflowStepTools.Where(t => t.WorkflowStepId == stepId).ToListAsync(ct);
        _db.WorkflowStepTools.RemoveRange(existing);
        foreach (var tid in toolIds.Distinct()) _db.WorkflowStepTools.Add(new WorkflowStepTool { WorkflowStepId = stepId, ToolId = tid });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("steps/{stepId:guid}/ragbuckets")]
    public async Task<IActionResult> SetStepRagBuckets(Guid stepId, [FromBody] Guid[] bucketIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var existing = await _db.WorkflowStepRagBuckets.Where(b => b.WorkflowStepId == stepId).ToListAsync(ct);
        _db.WorkflowStepRagBuckets.RemoveRange(existing);
        foreach (var bid in bucketIds.Distinct()) _db.WorkflowStepRagBuckets.Add(new WorkflowStepRagBucket { WorkflowStepId = stepId, RagBucketId = bid });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("steps/{stepId:guid}/files")]
    public async Task<IActionResult> AttachStepFile(Guid stepId, [FromBody] Guid fileId, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var ownsFile = await _db.UploadedFiles.AnyAsync(f => f.Id == fileId && (f.UserId == userId || f.UserId == null), ct);
        if (!ownsFile) return Forbid();
        var already = await _db.WorkflowStepFiles.AnyAsync(f => f.WorkflowStepId == stepId && f.UploadedFileId == fileId, ct);
        if (!already) _db.WorkflowStepFiles.Add(new WorkflowStepFile { WorkflowStepId = stepId, UploadedFileId = fileId });
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("steps/{stepId:guid}/files")]
    public async Task<IActionResult> ReplaceStepFiles(Guid stepId, [FromBody] Guid[] fileIds, CancellationToken ct)
    {
        var userId = ParseUserId();
        if (!userId.HasValue) return Unauthorized();
        var step = await _db.WorkflowSteps.FirstOrDefaultAsync(s => s.Id == stepId, ct);
        if (step == null) return NotFound();
        var isOwner = await _db.Workflows.AnyAsync(w => w.Id == step.WorkflowId && w.UserId == userId, ct);
        if (!isOwner) return Forbid();
        var existing = await _db.WorkflowStepFiles.Where(f => f.WorkflowStepId == stepId).ToListAsync(ct);
        _db.WorkflowStepFiles.RemoveRange(existing);
        foreach (var fileId in fileIds.Distinct())
        {
            var ownsFile = await _db.UploadedFiles.AnyAsync(f => f.Id == fileId && (f.UserId == userId || f.UserId == null), ct);
            if (ownsFile) _db.WorkflowStepFiles.Add(new WorkflowStepFile { WorkflowStepId = stepId, UploadedFileId = fileId });
        }
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    public record StepRequest(string? Name, string? UserInstruction, Guid? ModelId, Guid? RagBucketId, int? Order);

    private Guid? ParseUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }

    public record CreateRequest(string Name, string? Description, string? SystemInstruction, string? UserInstruction, Guid? ModelId, Guid? RagBucketId);
    public record UpdateRequest(string? Name, string? Description, string? SystemInstruction, string? UserInstruction, Guid? ModelId, Guid? RagBucketId, Guid[]? RagBucketIds = null, Guid[]? FileIds = null);
    public record ScheduleRequest(
        string? ScheduleType,
        int? IntervalMinutes,
        DateTimeOffset? RunAt,
        string? DaysOfWeek,
        int? DayOfMonth,
        int? WeekOfMonth,
        int? DayOfWeek,
        int? Hour,
        int? Minute,
        string? TimeZoneId,
        DateTimeOffset? EndAt,
        string? CronExpression);
}
