using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Fakes;

internal sealed class FakeJobSource(JobSource source) : IJobSource
{
    private readonly Queue<Func<JobSourceBatch>> _responses = new();

    public JobSource Source { get; } = source;

    public List<string?> ReceivedCheckpoints { get; } = [];

    public FakeJobSource Returns(string? checkpoint, params JobPosting[] postings)
    {
        _responses.Enqueue(() => new JobSourceBatch(postings, checkpoint));
        return this;
    }

    public FakeJobSource Throws(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    public Task<JobSourceBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken)
    {
        ReceivedCheckpoints.Add(checkpoint);
        var next = _responses.Count > 0 ? _responses.Dequeue() : () => new JobSourceBatch([], checkpoint);
        return Task.FromResult(next());
    }
}
