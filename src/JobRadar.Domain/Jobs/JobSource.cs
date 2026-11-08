namespace JobRadar.Domain.Jobs;

/// <summary>Origem de uma vaga. Cada fonte autorizada tem um id fixo.</summary>
public sealed class JobSource
{
    public static readonly JobSource LinkedIn = new(1, "LinkedIn");

    public static readonly IReadOnlyList<JobSource> All = [LinkedIn];

    public JobSource(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }
}
