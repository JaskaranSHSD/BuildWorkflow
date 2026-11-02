namespace BuildWorkflow.Models;

public class SqlToolConfiguration
{
    public Guid ToolId { get; set; }

    public ToolDefinition Tool { get; set; } = null!;

    public string DatabaseType { get; set; } = "PostgreSQL";

    public string ConnectionString { get; set; } = string.Empty;

    public string Query { get; set; } = string.Empty;
}