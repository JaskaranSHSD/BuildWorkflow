using BuildWorkflow.Models;

namespace BuildWorkflow.AICenter;

public class AiConnectorFactory
{
    private readonly IReadOnlyDictionary<string, IAiChatConnector> _connectors;

    public AiConnectorFactory(IEnumerable<IAiChatConnector> connectors)
    {
        _connectors = connectors.ToDictionary(c => c.Provider, c => c, StringComparer.OrdinalIgnoreCase);
    }

    public IAiChatConnector GetConnector(AiModelDefinition model)
    {
        if (_connectors.TryGetValue(model.Provider, out var connector))
        {
            return connector;
        }

        throw new NotSupportedException($"No AI connector is registered for provider '{model.Provider}'.");
    }
}