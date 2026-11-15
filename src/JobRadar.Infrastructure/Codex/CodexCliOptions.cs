using System.ComponentModel.DataAnnotations;
using JobRadar.Infrastructure.Claude;

namespace JobRadar.Infrastructure.Codex;

/// <summary>Codex CLI com login da conta, sem chave de API.</summary>
public sealed class CodexCliOptions
{
    public const string Section = "Codex";

    [Required]
    public string ExecutablePath { get; set; } = "codex";

    public string? Model { get; set; }

    [Range(5, 3600)]
    public int TimeoutSeconds { get; set; } = 180;

    [Range(1, 16)]
    public int MaxConcurrency { get; set; } = 2;

    [Range(1_000, 50_000_000)]
    public int MaxOutputChars { get; set; } = 1_000_000;

    public List<string> PassThroughEnvironment { get; set; } =
        [.. ClaudeCliOptions.DefaultPassThroughEnvironment.Where(name => !name.StartsWith("CLAUDE", StringComparison.Ordinal)), "CODEX_HOME"];

    public Dictionary<string, string> AdditionalEnvironment { get; set; } = [];

    public Dictionary<string, string> BuildEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in PassThroughEnvironment)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
            {
                environment[name] = value;
            }
        }

        foreach (var (name, value) in AdditionalEnvironment)
        {
            environment[name] = value;
        }

        return environment;
    }
}
