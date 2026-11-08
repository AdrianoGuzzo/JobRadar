using JobRadar.Application.Abstractions;

namespace JobRadar.UnitTests.Fakes;

/// <summary>Discord de mentira: guarda as mensagens enviadas e falha quando programado.</summary>
internal sealed class FakeDiscordNotificationService : IDiscordNotificationService
{
    private readonly Queue<Exception> _failures = new();

    private int _attempts;

    public List<JobNotificationMessage> Sent { get; } = [];

    public int Attempts => _attempts;

    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    public FakeDiscordNotificationService FailNext(Exception exception)
    {
        _failures.Enqueue(exception);
        return this;
    }

    public async Task<string> SendAsync(JobNotificationMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken);
        }

        lock (Sent)
        {
            if (_failures.TryDequeue(out var failure))
            {
                throw failure;
            }

            Sent.Add(message);
            return $"msg-{Sent.Count}";
        }
    }
}
