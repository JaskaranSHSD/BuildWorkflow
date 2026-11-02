using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace BuildWorkflow.Controllers;

public class SSOController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public SSOController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    [HttpGet("login/sso/backend")]
    public async Task<IActionResult> Backend(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest("SSO token is required.");
        }

        var validationEndpoint = _configuration["Sso:ValidationEndpoint"];
        if (string.IsNullOrWhiteSpace(validationEndpoint))
        {
            // Placeholder until the internal validation API is configured.
            validationEndpoint = "http://localhost/internal-sso-validate?token={token}";
        }

        var requestUrl = validationEndpoint.Contains("{token}", StringComparison.Ordinal)
            ? validationEndpoint.Replace("{token}", Uri.EscapeDataString(token))
            : $"{validationEndpoint.TrimEnd('?').TrimEnd('&')}{(validationEndpoint.Contains('?') ? "&" : "?")}token={Uri.EscapeDataString(token)}";

        using var client = new HttpClient();
        try
        {
            var response = await client.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode)
            {
                return Unauthorized("SSO token validation failed.");
            }

            var user = await response.Content.ReadFromJsonAsync<SsoUserResponse>();
            if (user == null || string.IsNullOrWhiteSpace(user.Username))
            {
                return Unauthorized("SSO user details are invalid.");
            }

            var username = user.Username.Trim();
            var role = string.IsNullOrWhiteSpace(user.Role) ? "User" : user.Role.Trim();
            var appUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);

            if (appUser == null)
            {
                appUser = new AppUser
                {
                    Username = username,
                    Password = Guid.NewGuid().ToString("N"),
                    Role = role
                };
                _db.Users.Add(appUser);
                await _db.SaveChangesAsync();
            }
            else if (!string.Equals(appUser.Role, role, StringComparison.Ordinal))
            {
                appUser.Role = role;
                await _db.SaveChangesAsync();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, appUser.Id.ToString()),
                new(ClaimTypes.Name, appUser.Username),
                new(ClaimTypes.Role, appUser.Role)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return RedirectToAction("Index", appUser.IsAdmin ? "Admin" : "Chat");
        }
        catch
        {
            return Unauthorized("SSO validation is not available.");
        }
    }

    private sealed class SsoUserResponse
    {
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
    }
}
