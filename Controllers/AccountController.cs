using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BuildWorkflow.Controllers;

public class AccountController : Controller
{
    private readonly AppDbContext _db;

    public AccountController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string username, string password, string? returnUrl = null)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Password == password);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.GivenName, user.FullName ?? user.Username),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.OrganizationId.HasValue)
        {
            claims.Add(new Claim("OrganizationId", user.OrganizationId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", user.IsAdmin ? "Admin" : "Chat");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var isAdmin = (User.IsInRole("Admin") || User.IsInRole("OrgAdmin") || User.IsInRole("PlatformAdmin"));

        ViewBag.User = await _db.Users.FindAsync(userId);
        ViewBag.Models = isAdmin
            ? await _db.AiModels.Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync()
            : await _db.UserAiModels.Where(a => a.UserId == userId).Include(a => a.AiModel).Select(a => a.AiModel).Where(m => m.IsActive).OrderBy(m => m.Name).ToListAsync();
        ViewBag.Tools = isAdmin
            ? await _db.Tools.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync()
            : await _db.UserToolPermissions.Where(a => a.UserId == userId).Include(a => a.Tool).Select(a => a.Tool).Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync();
        ViewBag.Buckets = isAdmin
            ? await _db.RagBuckets.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync()
            : await _db.UserRagBuckets.Where(a => a.UserId == userId).Include(a => a.RagBucket).Select(a => a.RagBucket).Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync();
        return View();
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}