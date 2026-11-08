using BuildWorkflow.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[Authorize]
public class WorkflowController : Controller
{
    private readonly AppDbContext _db;
    public WorkflowController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Manage(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var hasAccess = await _db.Workflows.AnyAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId)));
        if (!hasAccess) return NotFound();
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        ViewBag.WorkflowId = id;
        ViewBag.RagBuckets = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).ToListAsync()
            : await _db.UserRagBuckets.Where(a => a.UserId == userId).Include(a => a.RagBucket).Select(a => a.RagBucket).ToListAsync();
        ViewBag.Tools = isAdmin
            ? await _db.Tools.Where(t => t.IsActive).ToListAsync()
            : await _db.UserToolPermissions.Where(a => a.UserId == userId).Include(a => a.Tool).Select(a => a.Tool).ToListAsync();
        ViewBag.Models = isAdmin
            ? await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync()
            : await _db.UserAiModels.Where(a => a.UserId == userId).Include(a => a.AiModel).Where(a => a.AiModel.IsActive).Select(a => a.AiModel).OrderBy(m => m.Name).ToListAsync();
        var workflowUser = await _db.Users.Include(u => u.Organization).FirstOrDefaultAsync(u => u.Id == userId);
        ViewBag.AllowedFileExtensions = (workflowUser?.AllowedFileExtensions != null)
            ? (workflowUser.AllowedFileExtensions.Length == 0 ? string.Empty : workflowUser.AllowedFileExtensions)
            : workflowUser?.Organization?.AllowedFileExtensions;
        ViewBag.MaxUploadSizeBytes = workflowUser?.Organization?.MaxUploadSizeBytes ?? (10 * 1024 * 1024);
        return View();
    }

    public async Task<IActionResult> History(Guid id)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var hasAccess = await _db.Workflows.AnyAsync(w => w.Id == id && (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == id && s.UserId == userId)));
        if (!hasAccess) return NotFound();
        ViewBag.WorkflowId = id;
        return View();
    }

    public async Task<IActionResult> Scheduled(int page = 1, int pageSize = 10, string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        var query = from s in _db.WorkflowSchedules
                    join w in _db.Workflows on s.WorkflowId equals w.Id
                    where w.IsActive && (isAdmin || w.UserId == userId || _db.WorkflowShares.Any(sh => sh.WorkflowId == w.Id && sh.UserId == userId))
                    && (string.IsNullOrWhiteSpace(search) || w.Name.ToLower().Contains(search.Trim().ToLower()))
                    select new
                    {
                        ScheduleId = s.Id,
                        WorkflowId = w.Id,
                        WorkflowName = w.Name,
                        s.ScheduleType,
                        s.IntervalMinutes,
                        s.NextRunAt,
                        s.LastRunAt,
                        s.Enabled
                    };

        var ordered = query.OrderBy(x => x.NextRunAt == null).ThenBy(x => x.NextRunAt);
        var total = await ordered.CountAsync();
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Schedules = items;
        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.Total = total;
        ViewBag.Search = search;

        return View();
    }

    public async Task<IActionResult> Index()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        ViewBag.RagBuckets = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).ToListAsync()
            : await _db.UserRagBuckets.Where(a => a.UserId == userId).Include(a => a.RagBucket).Select(a => a.RagBucket).ToListAsync();
        ViewBag.Tools = isAdmin
            ? await _db.Tools.Where(t => t.IsActive).ToListAsync()
            : await _db.UserToolPermissions.Where(a => a.UserId == userId).Include(a => a.Tool).Select(a => a.Tool).ToListAsync();
        ViewBag.Models = isAdmin
            ? await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync()
            : await _db.UserAiModels.Where(a => a.UserId == userId).Include(a => a.AiModel).Where(a => a.AiModel.IsActive).Select(a => a.AiModel).OrderBy(m => m.Name).ToListAsync();

        ViewBag.Workflows = isAdmin
            ? await _db.Workflows.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync()
            : await _db.Workflows.Where(w => (w.UserId == userId || _db.WorkflowShares.Any(s => s.WorkflowId == w.Id && s.UserId == userId)) && w.IsActive).OrderBy(w => w.Name).ToListAsync();

        return View();
    }
}
