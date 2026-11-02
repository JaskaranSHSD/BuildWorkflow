namespace BuildWorkflow.Models;

public class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? AgentName { get; set; }

    public Guid? UserId { get; set; }

    public string Title { get; set; } = "New chat";

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChatMessageRecord> Messages { get; set; } = new List<ChatMessageRecord>();
}