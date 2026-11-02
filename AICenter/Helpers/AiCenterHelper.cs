namespace BuildWorkflow.AICenter.Helpers;

public static class AiCenterHelper
{
    public static string NormalizeProvider(string? provider) => string.IsNullOrWhiteSpace(provider) ? "Ollama" : provider.Trim();
}
