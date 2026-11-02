using Microsoft.AspNetCore.Http;

namespace BuildWorkflow.Services;

public sealed class OrganizationContext
{
    public bool IsPlatformAdmin { get; }

    public Guid? OrganizationId { get; }

    public OrganizationContext(IHttpContextAccessor httpContextAccessor)
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            // System/background work behaves as platform-level and is not tenant-scoped.
            IsPlatformAdmin = true;
            return;
        }

        IsPlatformAdmin = user.IsInRole("PlatformAdmin");
        var claim = user.FindFirst("OrganizationId")?.Value;
        if (Guid.TryParse(claim, out var organizationId))
        {
            OrganizationId = organizationId;
        }
    }
}