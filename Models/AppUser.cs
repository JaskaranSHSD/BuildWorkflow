namespace BuildWorkflow.Models;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string Password { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsAdmin => Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("OrgAdmin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("PlatformAdmin", StringComparison.OrdinalIgnoreCase);
}