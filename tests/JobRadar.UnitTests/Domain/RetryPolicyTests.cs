using JobRadar.Domain.Common;

namespace JobRadar.UnitTests.Domain;

public sealed class RetryPolicyTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private readonly RetryPolicy _policy = new(maxAttempts: 4, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromMinutes(3));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public void Should_Schedule_Retry_With_Backoff_On_Transient_Failure(int attemptsMade, int expectedMinutes)
    {
        var decision = _policy.Decide(attemptsMade, FailureKind.Transient, Now);

        decision.ShouldRetry.Should().BeTrue();
        decision.NextAttemptAt.Should().Be(Now.AddMinutes(expectedMinutes));
    }

    [Fact]
    public void Should_Cap_Delay_At_Maximum()
    {
        var decision = _policy.Decide(3, FailureKind.Transient, Now);

        decision.NextAttemptAt.Should().Be(Now.AddMinutes(3));
    }

    [Fact]
    public void Should_Give_Up_After_Max_Attempts()
    {
        var decision = _policy.Decide(4, FailureKind.Transient, Now);

        decision.ShouldRetry.Should().BeFalse();
        decision.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public void Should_Not_Retry_Permanent_Failure()
    {
        var decision = _policy.Decide(1, FailureKind.Permanent, Now);

        decision.ShouldRetry.Should().BeFalse();
    }

    [Fact]
    public void Should_Reject_Policy_Without_Attempts()
    {
        var act = () => new RetryPolicy(maxAttempts: 0, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        act.Should().Throw<DomainException>();
    }
}
