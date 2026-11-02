namespace BuildWorkflow.Models;

public class UploadedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public Guid? UserId { get; set; }

    public string SessionKey { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string StoredPath { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ExpiresAt { get; set; }
}
