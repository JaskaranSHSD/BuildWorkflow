namespace BuildWorkflow.Models;

public class UserToolPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public Guid ToolId { get; set; }

    public ToolDefinition Tool { get; set; } = null!;
}
