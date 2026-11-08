using JobRadar.Infrastructure.Claude;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.External;

/// <summary>
/// Contra o Claude Code real: consome cota da conta. Só roda com
/// <c>JOBRADAR_EXTERNAL_CLAUDE=1</c> (e, opcionalmente,
/// <c>JOBRADAR_EXTERNAL_CLAUDE_PATH</c>); sem isso, é pulado.
/// </summary>
[Trait("Category", "External")]
public sealed class RealClaudeTests
{
    [Fact]
    public async Task Should_Analyze_A_Job_With_The_Real_Cli()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("JOBRADAR_EXTERNAL_CLAUDE") == "1", "Defina JOBRADAR_EXTERNAL_CLAUDE=1 para rodar contra o Claude Code real.");

        var options = new ClaudeCliOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable("JOBRADAR_EXTERNAL_CLAUDE_PATH") ?? "claude",
            TimeoutSeconds = 300,
        };
        using var analyzer = new ClaudeCodeJobAnalyzer(Options.Create(options), NullLogger<ClaudeCodeJobAnalyzer>.Instance);

        var analysis = await analyzer.AnalyzeAsync(IntegrationData.Profile(), IntegrationData.Job(), TestContext.Current.CancellationToken);

        analysis.Score.Value.Should().BeInRange(0, 100);
        analysis.Summary.Should().NotBeNullOrWhiteSpace();
    }
}
