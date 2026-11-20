using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Domain;

public sealed class JobReviewTests
{
    [Theory]
    [InlineData(JobReviewStatus.Approved, JobReviewStatus.Discarded)]
    [InlineData(JobReviewStatus.Discarded, JobReviewStatus.Approved)]
    public void Repeating_A_Decision_Preserves_Its_Author_And_Time(JobReviewStatus first, JobReviewStatus second)
    {
        var now = DateTimeOffset.UnixEpoch;
        var review = new JobReview(Guid.NewGuid());
        review.Status.Should().Be(JobReviewStatus.Pending);
        review.Decide(first, "123", now);
        review.Decide(first, "456", now.AddMinutes(1));
        review.Status.Should().Be(first);
        review.DecidedBy.Should().Be("123");
        review.DecidedAt.Should().Be(now);

        review.Decide(second, "456", now.AddMinutes(2));
        review.Status.Should().Be(second);
        review.DecidedBy.Should().Be("456");
        review.DecidedAt.Should().Be(now.AddMinutes(2));
    }
}
