using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;

namespace JobRadar.UnitTests.Domain;

public sealed class CompatibilityScoreTests
{
    [Fact]
    public void Should_Reject_Score_Below_0()
    {
        var act = () => new CompatibilityScore(-1);

        act.Should().Throw<DomainException>().WithMessage("*0*100*");
    }

    [Fact]
    public void Should_Reject_Score_Above_100()
    {
        var act = () => new CompatibilityScore(101);

        act.Should().Throw<DomainException>().WithMessage("*0*100*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(75)]
    [InlineData(100)]
    public void Should_Accept_Scores_Within_Bounds(int value)
    {
        var score = new CompatibilityScore(value);

        score.Value.Should().Be(value);
    }
}
