using JobRadar.Domain.Common;
using JobRadar.Domain.Notifications;

namespace JobRadar.UnitTests.Domain;

public sealed class NotificationPolicyTests
{
    [Fact]
    public void Should_Use_75_As_Default_Threshold()
    {
        new NotificationPolicy().MinimumScore.Should().Be(75);
    }

    [Fact]
    public void Should_Not_Notify_When_Score_Below_Threshold()
    {
        var policy = new NotificationPolicy(minimumScore: 75);

        policy.ShouldNotify(TestData.Match(score: 74), alreadyNotified: false).Should().BeFalse();
    }

    [Fact]
    public void Should_Notify_When_Score_Equals_Threshold()
    {
        var policy = new NotificationPolicy(minimumScore: 75);

        policy.ShouldNotify(TestData.Match(score: 75), alreadyNotified: false).Should().BeTrue();
    }

    [Fact]
    public void Should_Not_Notify_When_Job_Was_Already_Notified()
    {
        var policy = new NotificationPolicy();

        policy.ShouldNotify(TestData.Match(score: 99), alreadyNotified: true).Should().BeFalse();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Should_Reject_Threshold_Outside_0_100(int minimumScore)
    {
        var act = () => new NotificationPolicy(minimumScore);

        act.Should().Throw<DomainException>();
    }
}
