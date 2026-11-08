using BuildWorkflow.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[Authorize]
public class ChatController : Controller
{
    private readonly AppDbContext _db;

    public ChatController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        ViewBag.RagBuckets = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).Include(b => b.KnowledgeGroup).OrderBy(b => b.Name).ToListAsync()
            : await _db.UserRagBuckets
                .Where(a => a.UserId == userId)
                .Include(a => a.RagBucket)
                .ThenInclude(b => b.KnowledgeGroup)
                .Select(a => a.RagBucket)
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

        ViewBag.Tools = isAdmin
            ? await _db.Tools.Where(t => t.IsActive).Include(t => t.ToolGroup).OrderBy(t => t.Name).ToListAsync()
            : await _db.UserToolPermissions
                .Where(a => a.UserId == userId)
                .Include(a => a.Tool)
                .ThenInclude(t => t.ToolGroup)
                .Select(a => a.Tool)
                .Where(t => t.IsActive)
                .OrderBy(t => t.Name)
                .ToListAsync();

        var assignedModels = await _db.UserAiModels
            .Where(a => a.UserId == userId)
            .Include(a => a.AiModel)
            .Where(a => a.AiModel.IsActive)
            .Select(a => a.AiModel)
            .OrderBy(m => m.Name)
            .ToListAsync();

        ViewBag.Models = assignedModels.Count > 0
            ? assignedModels
            : (isAdmin ? await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync() : assignedModels);

        var chatUser = await _db.Users.Include(u => u.Organization).FirstOrDefaultAsync(u => u.Id == userId);
        string? userAllowed = chatUser?.AllowedFileExtensions;
        string? orgAllowed = chatUser?.Organization?.AllowedFileExtensions;
        if (userAllowed != null)
        {
            ViewBag.AllowedFileExtensions = userAllowed.Length == 0 ? string.Empty : userAllowed;
        }
        else
        {
            ViewBag.AllowedFileExtensions = orgAllowed;
        }
        ViewBag.MaxUploadSizeBytes = chatUser?.Organization?.MaxUploadSizeBytes ?? (10 * 1024 * 1024);
        return View();
    }
}