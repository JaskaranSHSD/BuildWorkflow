using BuildWorkflow.Data;
using BuildWorkflow.Models;
using BuildWorkflow.Services;
using BuildWorkflow.Services;
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
    private readonly RsaKeyService _rsa;

    public AccountController(AppDbContext db, RsaKeyService rsa)
    {
        _db = db;
        _rsa = rsa;
    }

    [HttpGet]
    public IActionResult PublicKey()
    {
        return Json(new { publicKey = _rsa.GetPublicKeyBase64() });
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string? username = null, string? password = null, string? usernameEnc = null, string? passwordEnc = null, string? returnUrl = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(usernameEnc)) username = _rsa.Decrypt(usernameEnc);
            if (!string.IsNullOrWhiteSpace(passwordEnc)) password = _rsa.Decrypt(passwordEnc);
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Invalid encrypted credentials.");
            return View();
        }

        username = username?.Trim() ?? string.Empty;
        password = password?.Trim() ?? string.Empty;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user != null && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            ModelState.AddModelError(string.Empty, "Account is locked due to repeated failed login attempts.");
            return View();
        }

        if (user == null || !user.IsActive || user.Password != password)
        {
            if (user != null)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= 5)
                {
                    user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
                }
                await _db.SaveChangesAsync();
            }
            ModelState.AddModelError(string.Empty, "Invalid username or password.");
            return View();
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;

        var isPlatformUser = string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase);
        if (isPlatformUser)
        {
            user.MfaEnabled = true;
            user.MfaSecret = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(user.MfaSecret))
            {
                user.MfaSecret = MfaHelper.GenerateSecret();
            }
            user.MfaEnabled = true;
        }

        await _db.SaveChangesAsync();

        if (user.MfaEnabled)
        {
            HttpContext.Session.SetString("MfaPendingUserId", user.Id.ToString());
            HttpContext.Session.SetString("MfaReturnUrl", returnUrl ?? string.Empty);
            return RedirectToAction("MfaVerify");
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

        if (user.MustChangePassword)
        {
            return RedirectToAction("ChangePasswordRequired");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", user.IsAdmin ? "Admin" : "Chat");
    }

    [HttpGet]
    [HttpPost]
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
        var profileUser = ViewBag.User as AppUser;
        ViewBag.OrganizationName = profileUser?.OrganizationId.HasValue == true
            ? await _db.Organizations.Where(o => o.Id == profileUser.OrganizationId!.Value).Select(o => o.DisplayName ?? o.Name).FirstOrDefaultAsync()
            : null;
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(currentPassword) || user.Password != currentPassword)
        {
            TempData["ProfileError"] = "Current password is incorrect.";
            return RedirectToAction("Profile");
        }

        if (!IsStrongPassword(newPassword))
        {
            TempData["ProfileError"] = "Password must be at least 8 characters and include uppercase, lowercase, number, and special character.";
            return RedirectToAction("Profile");
        }

        if (newPassword != confirmPassword)
        {
            TempData["ProfileError"] = "New password and confirmation do not match.";
            return RedirectToAction("Profile");
        }

        user.Password = newPassword;
        await _db.SaveChangesAsync();
        TempData["ProfileMessage"] = "Password updated successfully.";
        return RedirectToAction("Profile");
    }

    [HttpGet]
    public IActionResult MfaVerify()
    {
        if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("MfaPendingUserId")))
        {
            return RedirectToAction("Login");
        }
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MfaVerify(string code, string? returnUrl = null)
    {
        var pendingId = HttpContext.Session.GetString("MfaPendingUserId");
        if (!Guid.TryParse(pendingId, out var userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null || !user.IsActive)
        {
            HttpContext.Session.Remove("MfaPendingUserId");
            HttpContext.Session.Remove("MfaReturnUrl");
            return RedirectToAction("Login");
        }

        var storedReturnUrl = HttpContext.Session.GetString("MfaReturnUrl") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            returnUrl = storedReturnUrl;
        }

        bool valid;
        if (string.Equals(user.Role, "PlatformAdmin", StringComparison.OrdinalIgnoreCase))
        {
            valid = code == MfaHelper.PlatformCode();
        }
        else
        {
            valid = MfaHelper.VerifyTotp(user.MfaSecret ?? string.Empty, code);
        }

        if (!valid)
        {
            TempData["MfaError"] = "Invalid MFA code.";
            return RedirectToAction("MfaVerify");
        }

        HttpContext.Session.Remove("MfaPendingUserId");
        HttpContext.Session.Remove("MfaReturnUrl");

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

        if (user.MustChangePassword)
        {
            return RedirectToAction("ChangePasswordRequired");
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction("Index", user.IsAdmin ? "Admin" : "Chat");
    }

    private static bool IsStrongPassword(string? password)
    {
        return !string.IsNullOrWhiteSpace(password) && password.Length >= 8 &&
            password.Any(char.IsUpper) && password.Any(char.IsLower) &&
            password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c));
    }

    [Authorize]
    public IActionResult ChangePasswordRequired()
    {
        return View();
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePasswordRequired(string newPassword, string confirmPassword)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound();
        }

        if (!IsStrongPassword(newPassword))
        {
            TempData["ProfileError"] = "Password must be at least 8 characters and include uppercase, lowercase, number, and special character.";
            return View();
        }

        if (newPassword != confirmPassword)
        {
            TempData["ProfileError"] = "New password and confirmation do not match.";
            return View();
        }

        user.Password = newPassword;
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();
        TempData["ProfileMessage"] = "Password updated successfully.";
        return RedirectToAction("Index", user.IsAdmin ? "Admin" : "Chat");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}