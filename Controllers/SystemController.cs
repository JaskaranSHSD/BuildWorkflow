using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace BuildWorkflow.Controllers;

[Authorize(Roles = "PlatformAdmin")]
public class SystemController : Controller
{
    private readonly IWebHostEnvironment _environment;
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public SystemController(IWebHostEnvironment environment, AppDbContext db, IConfiguration configuration)
    {
        _environment = environment;
        _db = db;
        _configuration = configuration;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetAll(string confirmPassword, CancellationToken cancellationToken)
    {
        var configuredPassword = _configuration["Reset:Password"];
        if (string.IsNullOrWhiteSpace(configuredPassword) || confirmPassword != configuredPassword)
        {
            TempData["Message"] = "Reset password is invalid or not configured in appsettings.json.";
            return RedirectToAction(nameof(Index));
        }

        var platformAdmin = await _db.Users.FirstOrDefaultAsync(u => u.Role == "PlatformAdmin", cancellationToken);
        var keepUserId = platformAdmin?.Id;
        var keepOrgId = platformAdmin?.OrganizationId;

        var tableNames = await _db.Database
            .SqlQueryRaw<string>(@"SELECT table_name::text FROM information_schema.tables WHERE table_schema = 'public' AND table_name NOT IN ('Users','Organizations','__EFMigrationsHistory')")
            .ToListAsync(cancellationToken);

        foreach (var tableName in tableNames)
        {
            await _db.Database.ExecuteSqlRawAsync($"TRUNCATE TABLE \"{tableName}\" RESTART IDENTITY CASCADE;", cancellationToken);
        }

        await _db.Users.Where(u => u.Id != keepUserId).ExecuteDeleteAsync(cancellationToken);
        if (keepOrgId.HasValue)
        {
            await _db.Organizations.Where(o => o.Id != keepOrgId.Value).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await _db.Organizations.ExecuteDeleteAsync(cancellationToken);
        }

        if (platformAdmin != null && keepOrgId.HasValue && platformAdmin.OrganizationId != keepOrgId)
        {
            platformAdmin.OrganizationId = keepOrgId;
            await _db.SaveChangesAsync(cancellationToken);
        }

        TempData["Message"] = "Application data reset completed. Only the platform admin and default organization remain.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult CleanTempFiles()
    {
        var root = Path.Combine(_environment.ContentRootPath, "TempFilesLocal");
        if (Directory.Exists(root))
        {
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                try { System.IO.File.Delete(file); } catch { }
            }
            foreach (var directory in Directory.GetDirectories(root))
            {
                try { Directory.Delete(directory, true); } catch { }
            }
        }

        TempData["Message"] = "Temporary files cleaned.";
        return RedirectToAction(nameof(Index));
    }
}
