namespace JobRadar.Domain.Jobs;

/// <summary>
/// O estado do monitoramento de uma fonte: até onde ela já foi lida
/// (<see cref="Checkpoint"/>, opaco, definido pela fonte) e quando foi
/// verificada pela última vez.
/// </summary>
public sealed class JobSearchConfiguration
{
    private JobSearchConfiguration()
    {
    }

    public int SourceId { get; private set; }

    public bool Enabled { get; private set; }

    public string? Checkpoint { get; private set; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public static JobSearchConfiguration Create(JobSource source) => new() { SourceId = source.Id, Enabled = true };

    public void RecordCheck(string? checkpoint, DateTimeOffset now)
    {
        Checkpoint = checkpoint;
        LastCheckedAt = now;
    }
}
