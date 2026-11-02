namespace BuildWorkflow.Models;

public class ToolDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string ToolType { get; set; } = "Api";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? ToolGroupId { get; set; }

    public ToolGroup? ToolGroup { get; set; }

    public ApiToolConfiguration? ApiConfiguration { get; set; }

    public SqlToolConfiguration? SqlConfiguration { get; set; }

    public SftpToolConfiguration? SftpConfiguration { get; set; }
}
