using JobRadar.Infrastructure.Claude;
using JobRadar.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace JobRadar.Infrastructure.Codex;

/// <summary>Verifica instalação e login sem fazer uma chamada ao modelo.</summary>
public sealed class CodexCliCheck(IOptions<CodexCliOptions> options)
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            foreach (var arguments in new string[][] { ["--version"], ["login", "status"] })
            {
                var result = await ProcessRunner.RunAsync(new ProcessRequest(settings.ExecutablePath, arguments,
                    string.Empty, Path.GetTempPath(), settings.BuildEnvironment(), TimeSpan.FromSeconds(30), 10_000), cancellationToken);
                if (arguments[0] == "--version" && result.ExitCode == 0)
                {
                    var text = result.StandardOutput.Trim().Replace("codex-cli ", string.Empty, StringComparison.Ordinal);
                    if (ClaudeCliVersionCheck.ParseVersion(text) is not { } version || version < new Version(0, 169, 0))
                    {
                        throw new InvalidOperationException("Atualize o Codex CLI para 0.169.0 ou superior.");
                    }
                }

                if (result.ExitCode != 0)
                {
                    throw new InvalidOperationException("Codex CLI indisponível ou sem login. Execute codex login (no Docker: docker compose run --rm worker codex login --device-auth).");
                }
            }
        }
        catch (Exception exception) when (exception is ProcessStartException or TimeoutException or ProcessOutputTooLargeException)
        {
            throw new InvalidOperationException("Codex CLI não encontrado ou sem resposta. Verifique Codex__ExecutablePath.", exception);
        }
    }
}
