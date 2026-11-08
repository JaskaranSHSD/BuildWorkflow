namespace BuildWorkflow.Models;

public class AiModelDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? OrganizationId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Provider { get; set; } = "Ollama";

    public string Endpoint { get; set; } = "http://localhost:11434";

    public string ModelName { get; set; } = string.Empty;

    public string? SystemPrompt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool SupportsTools { get; set; } = true;

    public bool SupportsImages { get; set; } = false;

    public bool IsEmbedding { get; set; } = false;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
