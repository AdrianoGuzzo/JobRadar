using JobRadar.Application.Jobs;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Application;

public sealed class ReviewJobsTests
{
    private readonly HashSet<ulong> _allowedUsers = [123];

    [Fact]
    public async Task Should_Reject_Unauthorized_Users_Before_Accessing_Repository()
    {
        var reviews = new ReviewJobs(new Repository(), TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;
        var decide = () => reviews.DecideAsync(Guid.NewGuid(), "999", JobReviewStatus.Approved, 456, _allowedUsers, ct);
        var list = () => reviews.ListAsync(1, 456, _allowedUsers, ct);
        await decide.Should().ThrowAsync<UnauthorizedAccessException>();
        await list.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Should_Use_The_Requested_Page_And_Report_Unavailable_Notification()
    {
        var repository = new Repository();
        var reviews = new ReviewJobs(repository, TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;
        (await reviews.DecideAsync(Guid.NewGuid(), "999", JobReviewStatus.Approved, 123, _allowedUsers, ct)).Should().BeNull();
        (await reviews.ListAsync(2, 123, _allowedUsers, ct)).Should().BeEmpty();
        repository.Page.Should().Be((5, 5));
    }

    private sealed class Repository : IJobReviewRepository
    {
        public (int Offset, int Limit) Page { get; private set; }

        public Task<JobReviewStatus?> DecideAsync(Guid notificationId, string messageId, JobReviewStatus status,
            string userId, TimeProvider time, CancellationToken cancellationToken) => Task.FromResult<JobReviewStatus?>(null);

        public Task<IReadOnlyList<ApprovedJob>> ListApprovedAsync(int offset, int limit, CancellationToken cancellationToken)
        {
            Page = (offset, limit);
            return Task.FromResult<IReadOnlyList<ApprovedJob>>([]);
        }
    }
}
