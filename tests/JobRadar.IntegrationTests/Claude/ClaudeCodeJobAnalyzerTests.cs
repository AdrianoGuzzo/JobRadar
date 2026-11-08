using System.Diagnostics;
using System.Text.Json;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Infrastructure.Claude;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.Claude;

/// <summary>
/// O analisador de verdade executando um processo de verdade: o
/// <c>JobRadar.FakeClaude</c>, que imita o <c>claude -p</c> por cenário.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ClaudeCodeJobAnalyzerTests : IDisposable
{
    private static readonly string FakeClaude =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "JobRadar.FakeClaude.exe" : "JobRadar.FakeClaude");

    private readonly DirectoryInfo _state = Directory.CreateTempSubdirectory("fake-claude-");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _state.Delete(recursive: true);

    [Fact]
    public async Task Should_Return_Analysis_On_Success()
    {
        using var analyzer = Analyzer("valid");

        var analysis = await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        analysis.Score.Value.Should().Be(92);
        analysis.Strengths.Should().Equal("Experiência com C# e .NET");
    }

    [Fact]
    public async Task Should_Read_Structured_Output()
    {
        using var analyzer = Analyzer("structured");

        var analysis = await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        analysis.Summary.Should().Be("Alta compatibilidade com o perfil profissional.");
    }

    [Fact]
    public async Task Should_Pass_Prompt_By_Stdin_And_Arguments_Without_Shell()
    {
        using var analyzer = Analyzer("echo");
        var hostileTitle = "Dev \"; rm -rf / ; echo $(whoami) `id` & del C:\\*";

        await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(title: hostileTitle), Ct);

        var invocation = ReadInvocation();
        invocation.Args.Should().StartWith(["-p", "--output-format", "json"]);
        invocation.Args.Should().Contain(["--tools", "", "--strict-mcp-config", "--safe-mode", "--restricted", "--no-session-persistence"]);
        invocation.Args.Should().NotContain(arg => arg.Contains("dangerously", StringComparison.OrdinalIgnoreCase));
        invocation.Args.Should().NotContain(arg => arg.Contains("rm -rf", StringComparison.Ordinal), "a vaga vai só pelo stdin");
        invocation.Stdin.Should().Contain("rm -rf /").And.Contain("Agro Tech");
    }

    [Fact]
    public async Task Should_Run_In_Empty_Temporary_Directory_With_Minimal_Environment()
    {
        Environment.SetEnvironmentVariable("JOBRADAR_TEST_SECRET", "não pode vazar");
        using var analyzer = Analyzer("echo");

        await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        var invocation = ReadInvocation();
        invocation.Env.Should().NotContain("JOBRADAR_TEST_SECRET");
        invocation.Env.Should().NotContain(name => name.StartsWith("ConnectionStrings", StringComparison.OrdinalIgnoreCase));
        invocation.Cwd.Should().NotStartWith(Directory.GetCurrentDirectory());
        Directory.Exists(invocation.Cwd).Should().BeFalse("o diretório temporário é apagado ao final");
    }

    [Fact]
    public async Task Should_Reject_Invalid_Json_As_Transient()
    {
        using var analyzer = Analyzer("invalid-json");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Fact]
    public async Task Should_Reject_Score_Out_Of_Range()
    {
        using var analyzer = Analyzer("score-out-of-range");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).WithMessage("*score*");
    }

    [Fact]
    public async Task Should_Classify_Authentication_Error_As_Permanent()
    {
        using var analyzer = Analyzer("error-auth");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Kind.Should().Be(FailureKind.Permanent);
    }

    [Fact]
    public async Task Should_Classify_Rate_Limit_As_Transient()
    {
        using var analyzer = Analyzer("error-rate-limit");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Fact]
    public async Task Should_Report_Exit_Code_And_Redacted_Stderr_On_Crash()
    {
        using var analyzer = Analyzer("crash");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        var exception = (await act.Should().ThrowAsync<JobAnalysisException>()).Which;
        exception.Kind.Should().Be(FailureKind.Transient);
        exception.Message.Should().Contain("código 3").And.Contain("panic");
        exception.Message.Should().NotContain("sk-ant-oat01-abcdefghijklmnopqrstuvwxyz");
    }

    [Fact]
    public async Task Should_Time_Out_And_Kill_Process_Tree()
    {
        using var analyzer = Analyzer("spawn-child", timeout: TimeSpan.FromSeconds(3));

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Message.Should().Contain("tempo limite");
        var childPid = int.Parse(await File.ReadAllTextAsync(Path.Combine(_state.FullName, "child.pid"), Ct), System.Globalization.CultureInfo.InvariantCulture);
        IsRunning(childPid).Should().BeFalse("o processo neto não pode ficar órfão");
    }

    [Fact]
    public async Task Should_Stop_When_Cancelled()
    {
        using var analyzer = Analyzer("hang", timeout: TimeSpan.FromMinutes(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var watch = Stopwatch.StartNew();

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Should_Fail_When_Output_Is_Too_Large()
    {
        using var analyzer = Analyzer("huge-output");

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Message.Should().Contain("limite");
    }

    [Fact]
    public async Task Should_Fail_Permanently_When_Executable_Is_Missing()
    {
        using var analyzer = new ClaudeCodeJobAnalyzer(
            Options.Create(new ClaudeCliOptions { ExecutablePath = Path.Combine(_state.FullName, "nao-existe.exe") }),
            NullLogger<ClaudeCodeJobAnalyzer>.Instance);

        var act = () => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct);

        (await act.Should().ThrowAsync<JobAnalysisException>()).Which.Kind.Should().Be(FailureKind.Permanent);
    }

    [Fact]
    public async Task Should_Limit_Concurrent_Processes()
    {
        using var analyzer = Analyzer("slow", maxConcurrency: 2);
        var maxSeen = 0;
        using var polling = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!polling.IsCancellationRequested)
            {
                maxSeen = Math.Max(maxSeen, Directory.GetFiles(_state.FullName, "running-*").Length);
                await Task.Delay(20, CancellationToken.None);
            }
        }, CancellationToken.None);

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), Ct)));
        await polling.CancelAsync();
        await poller;

        maxSeen.Should().BeInRange(1, 2);
    }

    private ClaudeCodeJobAnalyzer Analyzer(string scenario, TimeSpan? timeout = null, int maxConcurrency = 2)
    {
        Environment.SetEnvironmentVariable("FAKE_CLAUDE_SCENARIO", null);
        var options = new ClaudeCliOptions
        {
            ExecutablePath = FakeClaude,
            TimeoutSeconds = (int)(timeout ?? TimeSpan.FromSeconds(30)).TotalSeconds,
            MaxConcurrency = maxConcurrency,
            MaxOutputChars = 1_000_000,
        };
        options.AdditionalEnvironment["FAKE_CLAUDE_SCENARIO"] = scenario;
        options.AdditionalEnvironment["FAKE_CLAUDE_STATE_DIR"] = _state.FullName;

        return new ClaudeCodeJobAnalyzer(Options.Create(options), NullLogger<ClaudeCodeJobAnalyzer>.Instance);
    }

    private Invocation ReadInvocation() =>
        JsonSerializer.Deserialize<Invocation>(File.ReadAllText(Path.Combine(_state.FullName, "invocation.json")), JsonSerializerOptions.Web)!;

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private sealed record Invocation(string[] Args, string Stdin, string Cwd, string[] Env);
}
