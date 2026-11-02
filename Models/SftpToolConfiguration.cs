namespace BuildWorkflow.Models;

public class SftpToolConfiguration
{
    public Guid ToolId { get; set; }

    public ToolDefinition Tool { get; set; } = null!;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 22;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string RemoteBasePath { get; set; } = "/";
}