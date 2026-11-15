using System.Text.Json;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Infrastructure.Codex;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.Codex;

[Trait("Category", "Integration")]
public sealed class CodexJobAnalyzerTests : IDisposable
{
    private readonly DirectoryInfo _state = Directory.CreateTempSubdirectory("fake-codex-");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string Executable = Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "JobRadar.FakeClaude.exe" : "JobRadar.FakeClaude");

    [Fact]
    public async Task Should_Parse_Final_Analysis_And_Isolate_Process()
    {
        using var analyzer = Analyzer("echo");
        var analysis = await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);
        analysis.Score.Value.Should().Be(92);
        using var invocation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_state.FullName, "codex-invocation.json"), Ct));
        var root = invocation.RootElement;
        var args = root.GetProperty("args").EnumerateArray().Select(item => item.GetString()).ToArray();
        args.Should().Contain(["exec", "--ignore-user-config", "--ignore-rules", "--ephemeral", "read-only", "features.shell_tool=false"]);
        args.Should().NotContain(item => item!.Contains("dangerously", StringComparison.Ordinal));
        root.GetProperty("stdin").GetString().Should().Contain("Agro Tech");
        var env = root.GetProperty("env").EnumerateArray().Select(item => item.GetString()).ToArray();
        env.Should().NotContain(["OPENAI_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN", "DISCORD_BOT_TOKEN", "IMAP_PASSWORD"]);
        Directory.Exists(root.GetProperty("cwd").GetString()).Should().BeFalse();
    }

    [Theory]
    [InlineData("error-auth", FailureKind.Permanent)]
    [InlineData("error-rate-limit", FailureKind.Transient)]
    [InlineData("invalid-json", FailureKind.Transient)]
    [InlineData("missing-output", FailureKind.Transient)]
    public async Task Should_Classify_Failures(string scenario, FailureKind kind)
    {
        using var analyzer = Analyzer(scenario);
        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);
        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Kind.Should().Be(kind);
    }

    [Fact]
    public async Task Should_Check_Installation_And_Login_Without_Analysis()
    {
        var check = new CodexCliCheck(Options.Create(Settings("valid")));
        await check.CheckAsync(Ct);
    }

    [Fact]
    public async Task Should_Reject_Missing_Login()
    {
        var check = new CodexCliCheck(Options.Create(Settings("error-auth")));
        var act = () => check.CheckAsync(Ct);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*codex login*");
    }

    [Fact]
    public async Task Should_Propagate_Cancellation()
    {
        using var analyzer = Analyzer("hang");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), cancel.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private CodexJobAnalyzer Analyzer(string scenario) => new(Options.Create(Settings(scenario)));

    private CodexCliOptions Settings(string scenario) => new()
    {
        ExecutablePath = Executable,
        AdditionalEnvironment = new()
        {
            ["FAKE_CLAUDE_SCENARIO"] = scenario,
            ["FAKE_CLAUDE_VERSION"] = "codex-cli 0.169.0",
            ["FAKE_CLAUDE_STATE_DIR"] = _state.FullName,
        },
    };

    public void Dispose() => _state.Delete(recursive: true);
}
