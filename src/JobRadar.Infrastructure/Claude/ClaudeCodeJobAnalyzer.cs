using System.Globalization;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;
using JobRadar.Infrastructure.Processes;
using JobRadar.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobRadar.Infrastructure.Claude;

/// <summary>
/// <see cref="IJobAnalyzer"/> sobre o Claude Code CLI em modo não interativo
/// (ADR-003): sem ferramentas, sem MCP, sem CLAUDE.md nem hooks, num diretório
/// temporário vazio, com ambiente mínimo e no máximo
/// <see cref="ClaudeCliOptions.MaxConcurrency"/> processos ao mesmo tempo.
/// </summary>
public sealed partial class ClaudeCodeJobAnalyzer : IJobAnalyzer, IDisposable
{
    private const string OAuthTokenVariable = "CLAUDE_CODE_OAUTH_TOKEN";

    private readonly ClaudeCliOptions _options;

    private readonly ILogger<ClaudeCodeJobAnalyzer> _logger;

    private readonly SemaphoreSlim _gate;

    public ClaudeCodeJobAnalyzer(IOptions<ClaudeCliOptions> options, ILogger<ClaudeCodeJobAnalyzer> logger)
    {
        _options = options.Value;
        _logger = logger;
        _gate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    public async Task<JobAnalysis> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var workingDirectory = Directory.CreateTempSubdirectory("jobradar-claude-");

        try
        {
            var request = new ProcessRequest(
                _options.ExecutablePath,
                BuildArguments(_options),
                ClaudePromptBuilder.BuildUserPrompt(profile, job),
                workingDirectory.FullName,
                BuildEnvironment(_options),
                TimeSpan.FromSeconds(_options.TimeoutSeconds),
                _options.MaxOutputChars);

            var result = await RunAsync(request, cancellationToken);
            LogFinished(_logger, job.Id, result.ExitCode);

            if (result.ExitCode != 0)
            {
                throw ClaudeResponseParser.TryReadError(result.StandardOutput)
                    ?? new JobAnalysisException(
                        FailureKind.Transient,
                        $"O Claude Code terminou com código {result.ExitCode}: {Shorten(Redact(result.StandardError))}");
            }

            return ClaudeResponseParser.Parse(result.StandardOutput);
        }
        finally
        {
            TryDelete(workingDirectory);
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    /// <summary>A linha de comando, sempre a mesma forma; a vaga nunca entra aqui, só no stdin.</summary>
    public static IReadOnlyList<string> BuildArguments(ClaudeCliOptions options)
    {
        var arguments = new List<string>
        {
            "-p",
            "--output-format", "json",
            "--tools", string.Empty,
            "--strict-mcp-config",
            "--safe-mode",
            "--restricted",
            "--no-session-persistence",
            "--disable-slash-commands",
            "--max-turns", options.MaxTurns.ToString(CultureInfo.InvariantCulture),
            "--system-prompt", ClaudePromptBuilder.SystemPrompt,
        };

        if (options.UseJsonSchema)
        {
            arguments.AddRange(["--json-schema", ClaudePromptBuilder.JsonSchema]);
        }

        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            arguments.AddRange(["--model", options.Model]);
        }

        EnsureNoBypass(arguments);
        return arguments;
    }

    /// <summary>Nenhum argumento que pule permissões, venha de onde vier.</summary>
    public static void EnsureNoBypass(IEnumerable<string> arguments)
    {
        foreach (var argument in arguments)
        {
            if (argument.Contains("dangerously", StringComparison.OrdinalIgnoreCase)
                || argument.Contains("bypassPermissions", StringComparison.OrdinalIgnoreCase)
                || argument.Equals("--permission-mode", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("O analisador nunca executa o Claude pulando permissões.");
            }
        }
    }

    private static Dictionary<string, string> BuildEnvironment(ClaudeCliOptions options)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in options.PassThroughEnvironment)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                environment[name] = value;
            }
        }

        // Sem atualização automática nem tráfego acessório dentro do processo de análise.
        environment["DISABLE_AUTOUPDATER"] = "1";
        environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";

        foreach (var (name, value) in options.AdditionalEnvironment)
        {
            environment[name] = value;
        }

        return environment;
    }

    private async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await ProcessRunner.RunAsync(request, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new JobAnalysisException(FailureKind.Transient, $"O Claude excedeu o tempo limite de {_options.TimeoutSeconds} s.");
        }
        catch (ProcessStartException exception)
        {
            throw new JobAnalysisException(
                FailureKind.Permanent, $"Não foi possível executar o Claude Code em \"{_options.ExecutablePath}\". Verifique Claude__ExecutablePath.", exception);
        }
        catch (ProcessOutputTooLargeException exception)
        {
            throw new JobAnalysisException(FailureKind.Transient, $"A resposta do Claude passou do limite de tamanho ({exception.Message})");
        }
    }

    private static string Redact(string text) => SecretRedactor.Redact(text, Environment.GetEnvironmentVariable(OAuthTokenVariable));

    private static string Shorten(string text) => text.Length <= 500 ? text.Trim() : text[..500].Trim() + "…";

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Um neto ainda segurando arquivo: o diretório é temporário, o SO limpa.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Claude Code terminou a análise da vaga {JobId} com código {ExitCode}.")]
    private static partial void LogFinished(ILogger logger, Guid jobId, int exitCode);
}
