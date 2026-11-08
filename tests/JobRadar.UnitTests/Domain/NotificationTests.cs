using JobRadar.Domain.Common;
using JobRadar.Domain.Notifications;

namespace JobRadar.UnitTests.Domain;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private readonly RetryPolicy _retry = new(maxAttempts: 2, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromHours(1));

    [Fact]
    public void Should_Start_Pending_For_The_Match()
    {
        var match = TestData.Match();

        var notification = Notification.Create(match, Now);

        notification.JobId.Should().Be(match.JobId);
        notification.JobMatchId.Should().Be(match.Id);
        notification.CandidateProfileId.Should().Be(match.CandidateProfileId);
        notification.Status.Should().Be(NotificationStatus.Pending);
        notification.IsDue(Now).Should().BeTrue();
    }

    [Fact]
    public void Should_Lease_While_Sending_And_Release_When_Lease_Expires()
    {
        var notification = Notification.Create(TestData.Match(), Now);

        notification.StartSending(Now, Lease);

        notification.Status.Should().Be(NotificationStatus.Sending);
        notification.IsDue(Now.AddMinutes(1)).Should().BeFalse();
        notification.IsDue(Now.AddMinutes(3)).Should().BeTrue();
    }

    [Fact]
    public void Should_Mark_Sent_With_Message_Id()
    {
        var notification = Notification.Create(TestData.Match(), Now);
        notification.StartSending(Now, Lease);

        notification.MarkSent("1234567890", Now.AddSeconds(1));

        notification.Status.Should().Be(NotificationStatus.Sent);
        notification.ExternalMessageId.Should().Be("1234567890");
        notification.SentAt.Should().Be(Now.AddSeconds(1));
        notification.IsDue(Now.AddDays(1)).Should().BeFalse();
    }

    [Fact]
    public void Should_Schedule_Retry_When_Delivery_Fails_Transiently()
    {
        var notification = Notification.Create(TestData.Match(), Now);
        notification.StartSending(Now, Lease);

        notification.RecordFailure(FailureKind.Transient, "HTTP 503", _retry, Now);

        notification.Status.Should().Be(NotificationStatus.RetryScheduled);
        notification.Attempts.Should().Be(1);
        notification.LastError.Should().Be("HTTP 503");
        notification.IsDue(Now.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void Should_Give_Up_After_Max_Attempts()
    {
        var notification = Notification.Create(TestData.Match(), Now);
        notification.RecordFailure(FailureKind.Transient, "HTTP 503", _retry, Now);

        notification.RecordFailure(FailureKind.Transient, "HTTP 503", _retry, Now.AddMinutes(1));

        notification.Status.Should().Be(NotificationStatus.Failed);
        notification.IsDue(Now.AddDays(1)).Should().BeFalse();
    }
}
