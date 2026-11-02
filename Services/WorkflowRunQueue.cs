using System.Threading.Channels;

namespace BuildWorkflow.Services;

public class WorkflowRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public void Enqueue(Guid runId) => _channel.Writer.TryWrite(runId);

    public IAsyncEnumerable<Guid> DequeueAll(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
