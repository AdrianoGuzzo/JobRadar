using JobRadar.Infrastructure.Claude;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.Claude;

[Trait("Category", "Integration")]
public sealed class ClaudeCliVersionCheckTests
{
    private static readonly string FakeClaude =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "JobRadar.FakeClaude.exe" : "JobRadar.FakeClaude");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Report_Installed_Version()
    {
        var result = await Check("2.1.319 (Claude Code)");

        result.IsUsable.Should().BeTrue();
        result.Version.Should().Be(new Version(2, 1, 319));
        result.Problem.Should().BeNull();
    }

    [Fact]
    public async Task Should_Report_Outdated_Version()
    {
        var result = await Check("2.0.5 (Claude Code)");

        result.IsUsable.Should().BeFalse();
        result.Problem.Should().Contain("2.0.5").And.Contain(ClaudeCliVersionCheck.MinimumVersion.ToString());
    }

    [Fact]
    public async Task Should_Report_Unreadable_Version()
    {
        var result = await Check("versão desconhecida");

        result.IsUsable.Should().BeFalse();
        result.Problem.Should().Contain("versão");
    }

    [Fact]
    public async Task Should_Require_OAuth_Token_When_Configured_To()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN") is { Length: > 0 }, "O ambiente do teste já tem o token.");
        var options = new ClaudeCliOptions { ExecutablePath = FakeClaude, RequireOAuthToken = true };

        var result = await new ClaudeCliVersionCheck(Options.Create(options)).CheckAsync(Ct);

        result.IsUsable.Should().BeFalse();
        result.Problem.Should().Contain("CLAUDE_CODE_OAUTH_TOKEN").And.Contain("claude setup-token");
    }

    [Fact]
    public async Task Should_Accept_OAuth_Token_When_Required_And_Present()
    {
        var options = new ClaudeCliOptions { ExecutablePath = FakeClaude, RequireOAuthToken = true };
        options.AdditionalEnvironment["CLAUDE_CODE_OAUTH_TOKEN"] = "sk-ant-oat01-token-de-teste";

        var result = await new ClaudeCliVersionCheck(Options.Create(options)).CheckAsync(Ct);

        result.IsUsable.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Report_Missing_Executable()
    {
        var check = new ClaudeCliVersionCheck(Options.Create(new ClaudeCliOptions { ExecutablePath = Path.Combine(Path.GetTempPath(), "nao-existe-claude.exe") }));

        var result = await check.CheckAsync(Ct);

        result.IsUsable.Should().BeFalse();
        result.Problem.Should().Contain("Claude__ExecutablePath");
    }

    private static Task<ClaudeCliCheckResult> Check(string reportedVersion)
    {
        var options = new ClaudeCliOptions { ExecutablePath = FakeClaude };
        options.AdditionalEnvironment["FAKE_CLAUDE_VERSION"] = reportedVersion;
        return new ClaudeCliVersionCheck(Options.Create(options)).CheckAsync(Ct);
    }
}
