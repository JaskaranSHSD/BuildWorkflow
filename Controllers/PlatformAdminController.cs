using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BuildWorkflow.Controllers;

[Authorize(Roles = "PlatformAdmin")]
public class PlatformAdminController : Controller
{
    private readonly AppDbContext _db;

    public PlatformAdminController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int page = 1, string? search = null)
    {
        const int pageSize = 50;
        page = page < 1 ? 1 : page;
        var query = _db.Organizations.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o => (o.Name ?? "").ToLower().Contains(term) || (o.Slug ?? "").ToLower().Contains(term) || (o.DisplayName ?? "").ToLower().Contains(term));
        }
        query = query.OrderBy(o => o.Name);
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        if (page > totalPages) page = totalPages;
        var organizations = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalCount = totalCount;
        ViewBag.TotalPages = totalPages;
        ViewBag.Search = search;
        ViewBag.UserCounts = await _db.Users
            .Where(u => u.OrganizationId.HasValue)
            .GroupBy(u => u.OrganizationId!.Value)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrganizationId, x => x.Count);

        return View(organizations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrganization(string name, string? slug, string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Organizations.Add(new Organization
            {
                Name = name.Trim(),
                Slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
                MaxUploadSizeBytes = 10 * 1024 * 1024,
                AllowedFileExtensions = ".pdf,.doc,.docx,.xls,.xlsx,.csv,.txt,.png,.jpg,.jpeg,.gif,.zip"
            });
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> EditOrganization(Guid id)
    {
        var organization = await _db.Organizations.FindAsync(id);
        if (organization == null)
        {
            return NotFound();
        }

        return View(organization);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditOrganization(Guid id, string name, string? slug, string? displayName, long? maxUploadSizeBytes, string[]? allowedFileExtensions, bool isActive)
    {
        var organization = await _db.Organizations.FindAsync(id);
        if (organization == null || string.IsNullOrWhiteSpace(name))
        {
            return RedirectToAction(nameof(Index));
        }

        organization.Name = name.Trim();
        organization.Slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim();
        organization.DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (maxUploadSizeBytes.HasValue && maxUploadSizeBytes.Value > 0)
        {
            organization.MaxUploadSizeBytes = maxUploadSizeBytes.Value;
        }
        if (allowedFileExtensions != null)
        {
            organization.AllowedFileExtensions = string.Join(",", allowedFileExtensions);
        }
        organization.IsActive = isActive;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ManageOrganization(Guid id)
    {
        var organization = await _db.Organizations.FindAsync(id);
        if (organization == null)
        {
            return NotFound();
        }

        ViewBag.Organization = organization;
        ViewBag.Users = await _db.Users
            .Where(u => u.OrganizationId == id)
            .OrderBy(u => u.Username)
            .ToListAsync();

        return View(organization);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(Guid organizationId, string username, string password, string role, string? fullName, string? email)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return RedirectToAction(nameof(ManageOrganization), new { id = organizationId });
        }

        var cleanUsername = username.Trim();
        var cleanEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        var duplicate = _db.Users.Any(u =>
            u.Username == cleanUsername ||
            (cleanEmail != null && u.Email == cleanEmail));
        if (duplicate)
        {
            return RedirectToAction(nameof(ManageOrganization), new { id = organizationId });
        }

        _db.Users.Add(new AppUser
        {
            Username = cleanUsername,
            Password = password,
            Role = string.IsNullOrWhiteSpace(role) ? "User" : role,
            FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
            Email = cleanEmail,
            OrganizationId = organizationId,
            MustChangePassword = true
        });
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(ManageOrganization), new { id = organizationId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(Guid id, Guid organizationId)
    {
        var user = await _db.Users.FindAsync(id);
        if (user != null && user.OrganizationId == organizationId)
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(ManageOrganization), new { id = organizationId });
    }
}