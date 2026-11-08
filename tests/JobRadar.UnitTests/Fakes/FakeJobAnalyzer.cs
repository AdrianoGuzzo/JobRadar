using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.UnitTests.Fakes;

/// <summary>Analisador programável: responde por título da vaga e mede a concorrência.</summary>
internal sealed class FakeJobAnalyzer : IJobAnalyzer
{
    private readonly Dictionary<string, Queue<Func<JobAnalysis>>> _byTitle = [];

    private int _running;

    private int _calls;

    public int Calls => _calls;

    public int MaxConcurrent { get; private set; }

    public TimeSpan Delay { get; init; } = TimeSpan.Zero;

    public List<(Guid ProfileId, Guid JobId)> Received { get; } = [];

    public FakeJobAnalyzer For(string title, Func<JobAnalysis> response)
    {
        if (!_byTitle.TryGetValue(title, out var queue))
        {
            queue = new Queue<Func<JobAnalysis>>();
            _byTitle[title] = queue;
        }

        queue.Enqueue(response);
        return this;
    }

    public async Task<JobAnalysis> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var running = Interlocked.Increment(ref _running);

        lock (Received)
        {
            MaxConcurrent = Math.Max(MaxConcurrent, running);
            Received.Add((profile.Id, job.Id));
        }

        try
        {
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            Func<JobAnalysis> response;

            lock (_byTitle)
            {
                response = _byTitle.TryGetValue(job.Title, out var queue) && queue.Count > 0
                    ? (queue.Count > 1 ? queue.Dequeue() : queue.Peek())
                    : () => TestData.Analysis();
            }

            return response();
        }
        finally
        {
            Interlocked.Decrement(ref _running);
        }
    }
}
