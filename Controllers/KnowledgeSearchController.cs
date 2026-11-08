using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

[Authorize]
public class KnowledgeSearchController : Controller
{
    private readonly AppDbContext _db;

    public KnowledgeSearchController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin");

        var dbUser = await _db.Users.FindAsync(userId);
        if (!isAdmin && (dbUser == null || !dbUser.HasModulePermission(UserModulePermission.RagRead)))
        {
            return Forbid();
        }

        var buckets = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct)
            : await _db.UserRagBuckets.Where(a => a.UserId == userId)
                .Include(a => a.RagBucket)
                .Select(a => a.RagBucket)
                .Where(b => b != null && b.IsActive)
                .OrderBy(b => b!.Name)
                .ToListAsync(ct);

        ViewBag.Buckets = (buckets ?? new List<RagBucket>()).Cast<RagBucket>().ToList();
        return View();
    }
}
