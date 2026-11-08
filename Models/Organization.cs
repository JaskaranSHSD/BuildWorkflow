namespace BuildWorkflow.Models;

public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string? Slug { get; set; }

    public string? DisplayName { get; set; }

    public string? AllowedFileExtensions { get; set; }

    public long MaxUploadSizeBytes { get; set; } = 10 * 1024 * 1024;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
