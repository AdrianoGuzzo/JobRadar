using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;

namespace JobRadar.UnitTests.Domain;

public sealed class JobIdentityTests
{
    [Fact]
    public void Should_Produce_Same_Fingerprint_For_Equivalent_Jobs()
    {
        var original = JobFingerprint.Compute("Desenvolvedor .NET Sênior", "Agro Tech", "São Paulo, SP");
        var variant = JobFingerprint.Compute("  desenvolvedor   .net SENIOR ", "AGRO  TECH", "Sao Paulo,  SP");

        variant.Should().Be(original);
    }

    [Fact]
    public void Should_Produce_Different_Fingerprint_For_Different_Companies()
    {
        var first = JobFingerprint.Compute("Desenvolvedor .NET", "Empresa A", "Remoto");
        var second = JobFingerprint.Compute("Desenvolvedor .NET", "Empresa B", "Remoto");

        second.Should().NotBe(first);
    }

    [Fact]
    public void Should_Compute_Fingerprint_When_Company_And_Location_Are_Missing()
    {
        var fingerprint = JobFingerprint.Compute("Desenvolvedor .NET", null, null);

        fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Should_Strip_Tracking_Parameters_From_Url()
    {
        var url = JobUrl.Normalize(
            "https://WWW.LinkedIn.com/comm/jobs/view/4012345678/?trackingId=abc%3D%3D&refId=xyz&lipi=urn&midToken=t&utm_source=email&currentJobId=4012345678#top");

        url.Should().Be("https://www.linkedin.com/comm/jobs/view/4012345678/?currentJobId=4012345678");
    }

    [Fact]
    public void Should_Keep_Url_Without_Query_Unchanged()
    {
        JobUrl.Normalize("https://example.com/vagas/123").Should().Be("https://example.com/vagas/123");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/vaga")]
    [InlineData("javascript:alert(1)")]
    public void Should_Reject_Invalid_Url(string url)
    {
        var act = () => JobUrl.Normalize(url);

        act.Should().Throw<DomainException>();
    }
}
