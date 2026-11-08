using JobRadar.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace JobRadar.Infrastructure.Claude;

public sealed record ClaudeCliCheckResult(bool IsUsable, Version? Version, string? Problem);

/// <summary>
/// Confere no startup que o Claude Code existe e é de uma versão em que os
/// flags de isolamento foram verificados (ADR-003). Não consome cota da conta.
/// </summary>
public sealed class ClaudeCliVersionCheck(IOptions<ClaudeCliOptions> options)
{
    /// <summary>A versão em que <c>--safe-mode</c>, <c>--restricted</c> e <c>--json-schema</c> foram conferidos.</summary>
    public static readonly Version MinimumVersion = new(2, 1, 319);

    public async Task<ClaudeCliCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in settings.PassThroughEnvironment)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in settings.AdditionalEnvironment)
        {
            environment[name] = value;
        }

        if (settings.RequireOAuthToken && !environment.TryGetValue("CLAUDE_CODE_OAUTH_TOKEN", out _))
        {
            return new ClaudeCliCheckResult(
                false, null, "Defina CLAUDE_CODE_OAUTH_TOKEN (gere com \"claude setup-token\" numa máquina com navegador e coloque no .env).");
        }

        ProcessResult result;

        try
        {
            result = await ProcessRunner.RunAsync(
                new ProcessRequest(settings.ExecutablePath, ["--version"], string.Empty, Path.GetTempPath(), environment, TimeSpan.FromSeconds(30), 10_000),
                cancellationToken);
        }
        catch (Exception exception) when (exception is ProcessStartException or TimeoutException)
        {
            return new ClaudeCliCheckResult(false, null, $"Claude Code não encontrado ou sem resposta em \"{settings.ExecutablePath}\". Verifique Claude__ExecutablePath.");
        }

        var text = result.StandardOutput.Trim();

        if (result.ExitCode != 0 || ParseVersion(text) is not { } version)
        {
            return new ClaudeCliCheckResult(false, null, $"Não foi possível ler a versão do Claude Code (\"{text}\").");
        }

        return version < MinimumVersion
            ? new ClaudeCliCheckResult(false, version, $"Claude Code {version} é anterior à {MinimumVersion}, a versão mínima verificada. Atualize o CLI.")
            : new ClaudeCliCheckResult(true, version, null);
    }

    /// <summary>O <c>x.y.z</c> do começo da saída de <c>claude --version</c>.</summary>
    public static Version? ParseVersion(string text)
    {
        var numeric = new string([.. text.TakeWhile(c => char.IsDigit(c) || c == '.')]).Trim('.');
        return Version.TryParse(numeric, out var version) ? version : null;
    }
}
