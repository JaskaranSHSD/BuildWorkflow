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

    public async Task<IActionResult> Index()
    {
        var organizations = await _db.Organizations
            .OrderBy(o => o.Name)
            .ToListAsync();

        ViewBag.UserCounts = await _db.Users
            .Where(u => u.OrganizationId.HasValue)
            .GroupBy(u => u.OrganizationId!.Value)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OrganizationId, x => x.Count);

        return View(organizations);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrganization(string name, string? slug)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _db.Organizations.Add(new Organization
            {
                Name = name.Trim(),
                Slug = string.IsNullOrWhiteSpace(slug) ? null : slug.Trim()
            });
            await _db.SaveChangesAsync();
        }

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

        if (!_db.Users.Any(u => u.Username == username))
        {
            _db.Users.Add(new AppUser
            {
                Username = username.Trim(),
                Password = password,
                Role = string.IsNullOrWhiteSpace(role) ? "User" : role,
                FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim(),
                Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
                OrganizationId = organizationId
            });
            await _db.SaveChangesAsync();
        }

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