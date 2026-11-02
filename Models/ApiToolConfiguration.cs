namespace BuildWorkflow.Models;

public class ApiToolConfiguration
{
    public Guid ToolId { get; set; }

    public ToolDefinition Tool { get; set; } = null!;

    public string HttpMethod { get; set; } = "GET";

    public string Endpoint { get; set; } = string.Empty;

    public string? HeadersJson { get; set; }

    public string? AuthType { get; set; } = "None";

    public string? Credential { get; set; }

    public string? RequestTemplate { get; set; }

    public string? ResponsePath { get; set; }
}
