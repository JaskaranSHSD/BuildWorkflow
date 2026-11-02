namespace BuildWorkflow.Models;

public class UserAiModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public AppUser User { get; set; } = null!;

    public Guid AiModelId { get; set; }

    public AiModelDefinition AiModel { get; set; } = null!;
}
