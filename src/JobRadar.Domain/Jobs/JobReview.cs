namespace JobRadar.Domain.Jobs;

public enum JobReviewStatus
{
    Pending,
    Approved,
    Discarded,
}

public sealed class JobReview(Guid jobId)
{
    public Guid JobId { get; private set; } = jobId;
    public JobReviewStatus Status { get; private set; }
    public string DecidedBy { get; private set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; private set; }

    public void Decide(JobReviewStatus status, string userId, DateTimeOffset now)
    {
        if (status is not (JobReviewStatus.Approved or JobReviewStatus.Discarded))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (Status == status)
        {
            return;
        }

        Status = status;
        DecidedBy = userId;
        DecidedAt = now;
    }
}
