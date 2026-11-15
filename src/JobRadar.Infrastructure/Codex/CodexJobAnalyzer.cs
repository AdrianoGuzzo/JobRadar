using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;
using JobRadar.Infrastructure.Claude;
using JobRadar.Infrastructure.Processes;
using Microsoft.Extensions.Options;

namespace JobRadar.Infrastructure.Codex;

/// <summary>Executa codex exec isolado do projeto e com saída estruturada.</summary>
public sealed class CodexJobAnalyzer(IOptions<CodexCliOptions> options) : IJobAnalyzer, IDisposable
{
    private readonly CodexCliOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(options.Value.MaxConcurrency, options.Value.MaxConcurrency);

    public async Task<JobAnalysis> AnalyzeAsync(CandidateProfile profile, Job job, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        DirectoryInfo? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("jobradar-codex-");
            var schema = Path.Combine(directory.FullName, "schema.json");
            var output = Path.Combine(directory.FullName, "analysis.json");
            await File.WriteAllTextAsync(schema, ClaudePromptBuilder.JsonSchema, cancellationToken);
            var result = await ProcessRunner.RunAsync(new ProcessRequest(
                _options.ExecutablePath, BuildArguments(_options, schema, output),
                ClaudePromptBuilder.SystemPrompt + "\n\n" + ClaudePromptBuilder.BuildUserPrompt(profile, job),
                directory.FullName, _options.BuildEnvironment(), TimeSpan.FromSeconds(_options.TimeoutSeconds),
                _options.MaxOutputChars), cancellationToken);

            // Não persiste stderr: pode conter dados da vaga, do perfil ou da autenticação.
            if (result.ExitCode != 0)
            {
                throw new JobAnalysisException(ClaudeResponseParser.Classify(result.StandardError + result.StandardOutput, null),
                    $"O Codex CLI terminou com código {result.ExitCode}. Confira o login com codex login status.");
            }

            if (!File.Exists(output) || new FileInfo(output).Length > _options.MaxOutputChars)
            {
                throw new JobAnalysisException(FailureKind.Transient, "O Codex não produziu uma análise dentro do limite de tamanho.");
            }

            return ClaudeResponseParser.Parse(await File.ReadAllTextAsync(output, cancellationToken));
        }
        catch (TimeoutException)
        {
            throw new JobAnalysisException(FailureKind.Transient, $"O Codex excedeu o tempo limite de {_options.TimeoutSeconds} s.");
        }
        catch (ProcessStartException exception)
        {
            throw new JobAnalysisException(FailureKind.Permanent, "Não foi possível executar o Codex CLI. Verifique Codex__ExecutablePath.", exception);
        }
        catch (ProcessOutputTooLargeException)
        {
            throw new JobAnalysisException(FailureKind.Transient, "A saída do Codex passou do limite de tamanho.");
        }
        finally
        {
            if (directory is not null)
            {
                try { directory.Delete(recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            _gate.Release();
        }
    }

    public static IReadOnlyList<string> BuildArguments(CodexCliOptions options, string schema, string output)
    {
        var arguments = new List<string>
        {
            "exec", "--ignore-user-config", "--ignore-rules", "--skip-git-repo-check", "--ephemeral",
            "--sandbox", "read-only", "--color", "never",
            "-c", "features.shell_tool=false", "-c", "web_search=\"disabled\"",
            "--output-schema", schema, "--output-last-message", output,
        };
        if (!string.IsNullOrWhiteSpace(options.Model))
        {
            arguments.AddRange(["--model", options.Model]);
        }

        arguments.Add("-");
        return arguments;
    }

    public void Dispose() => _gate.Dispose();
}
