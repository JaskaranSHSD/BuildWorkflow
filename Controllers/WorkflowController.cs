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

    public IActionResult History(Guid id)
    {
        ViewBag.WorkflowId = id;
        return View();
    }

    public IActionResult ChainGraph()
    {
        ViewBag.RunId = Request.Query["runId"].ToString();
        ViewBag.WorkflowId = Request.Query["workflowId"].ToString();
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
