using BuildWorkflow.Data;
using BuildWorkflow.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Security.Claims;

namespace BuildWorkflow.Filters;

public class MustChangePasswordFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var path = (context.HttpContext.Request.Path.Value ?? string.Empty).ToLowerInvariant();
        if (path.Contains("/account/changepassword") || path.Contains("/account/logout") || path.Contains("/account/login") || path.Contains("/account/accessdenied"))
        {
            return;
        }

        var userIdValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        var db = context.HttpContext.RequestServices.GetService<AppDbContext>();
        var appUser = db?.Users.Find(userId);
        if (appUser?.MustChangePassword == true)
        {
            context.Result = new RedirectToActionResult("ChangePasswordRequired", "Account", null);
        }
    }
}
