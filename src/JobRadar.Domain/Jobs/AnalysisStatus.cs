namespace JobRadar.Domain.Jobs;

public enum AnalysisStatus
{
    Pending,
    Analyzing,
    RetryScheduled,
    Analyzed,
    Failed,
}
