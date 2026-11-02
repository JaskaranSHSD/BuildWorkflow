namespace BuildWorkflow.Models;

public class ChatWorkflowStep
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkflowId { get; set; }

    public int StepOrder { get; set; }

    public string UserMessage { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
