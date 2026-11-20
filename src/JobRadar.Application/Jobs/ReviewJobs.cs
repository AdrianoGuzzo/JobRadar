using System.Globalization;
using JobRadar.Domain.Jobs;

namespace JobRadar.Application.Jobs;

public sealed record ApprovedJob(string Title, string? Company, string Url);

public interface IJobReviewRepository
{
    Task<JobReviewStatus?> DecideAsync(Guid notificationId, string messageId, JobReviewStatus status, string userId, TimeProvider time, CancellationToken cancellationToken);

    Task<IReadOnlyList<ApprovedJob>> ListApprovedAsync(int offset, int limit, CancellationToken cancellationToken);
}

public sealed class ReviewJobs(IJobReviewRepository reviews, TimeProvider time)
{
    public const int PageSize = 5;
    public const int MaxPage = int.MaxValue / PageSize;

    public bool IsAuthorized(ulong userId, IReadOnlySet<ulong> allowedUsers) =>
        userId != 0 && allowedUsers.Contains(userId);

    public Task<JobReviewStatus?> DecideAsync(Guid notificationId, string messageId, JobReviewStatus status,
        ulong userId, IReadOnlySet<ulong> allowedUsers, CancellationToken cancellationToken)
    {
        Authorize(userId, allowedUsers);
        return reviews.DecideAsync(notificationId, messageId, status, userId.ToString(CultureInfo.InvariantCulture), time, cancellationToken);
    }

    public Task<IReadOnlyList<ApprovedJob>> ListAsync(int page, ulong userId, IReadOnlySet<ulong> allowedUsers, CancellationToken cancellationToken)
    {
        Authorize(userId, allowedUsers);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(page, MaxPage);
        return reviews.ListApprovedAsync((page - 1) * PageSize, PageSize, cancellationToken);
    }

    private void Authorize(ulong userId, IReadOnlySet<ulong> allowedUsers)
    {
        if (!IsAuthorized(userId, allowedUsers))
        {
            throw new UnauthorizedAccessException();
        }
    }
}
