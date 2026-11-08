namespace BuildWorkflow.Models;

using System.ComponentModel.DataAnnotations.Schema;
using BuildWorkflow.Services;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;

    [NotMapped]
    public string Password
    {
        get => string.Empty;
        set => PasswordHash = string.IsNullOrWhiteSpace(value) ? string.Empty : PasswordHasher.Hash(value);
    }

    public string Role { get; set; } = "User";

    public Guid? OrganizationId { get; set; }

    public Organization? Organization { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public int FailedLoginAttempts { get; set; }

    public DateTimeOffset? LockoutEnd { get; set; }

    public bool MustChangePassword { get; set; }

    public bool IsActive { get; set; } = true;

    public string? AllowedFileExtensions { get; set; }

    public string? MfaSecret { get; set; }

    public bool MfaEnabled { get; set; } = true;

    public long Permissions { get; set; } = 0;

    public bool IsAdmin => Role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("OrgAdmin", StringComparison.OrdinalIgnoreCase)
        || Role.Equals("PlatformAdmin", StringComparison.OrdinalIgnoreCase);
}