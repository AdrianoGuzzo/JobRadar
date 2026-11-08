using System.ComponentModel.DataAnnotations;

namespace JobRadar.Infrastructure.Claude;

/// <summary>Configuração do Claude Code CLI (seção <c>Claude</c>, variáveis <c>Claude__*</c>).</summary>
public sealed class ClaudeCliOptions
{
    public const string Section = "Claude";

    /// <summary>
    /// Variáveis que o processo herda. Tudo o mais é removido, inclusive a
    /// connection string, o token do Discord e a senha do IMAP.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPassThroughEnvironment =
    [
        "PATH", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "SystemRoot", "SYSTEMROOT", "TEMP", "TMP", "LANG", "LC_ALL",
        "CLAUDE_CODE_OAUTH_TOKEN", "CLAUDE_CONFIG_DIR",
    ];

    /// <summary>Caminho do executável. No Windows, aponte para o <c>claude.exe</c> (um <c>.cmd</c> exigiria shell).</summary>
    [Required]
    public string ExecutablePath { get; set; } = "claude";

    /// <summary>Modelo (<c>--model</c>); vazio usa o padrão da conta.</summary>
    public string? Model { get; set; }

    [Range(5, 3600)]
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Quantos processos do Claude rodam ao mesmo tempo, no máximo.</summary>
    [Range(1, 16)]
    public int MaxConcurrency { get; set; } = 2;

    /// <summary>A saída estruturada pode gastar um turno extra; nenhuma ferramenta está liberada.</summary>
    [Range(1, 10)]
    public int MaxTurns { get; set; } = 3;

    /// <summary>Envia o <c>--json-schema</c> para o Claude validar a resposta.</summary>
    public bool UseJsonSchema { get; set; } = true;

    /// <summary>
    /// Exige <c>CLAUDE_CODE_OAUTH_TOKEN</c> no startup (ligado na imagem Docker, onde
    /// não há login interativo). Sem ele, toda análise falharia por autenticação.
    /// </summary>
    public bool RequireOAuthToken { get; set; }

    [Range(1_000, 50_000_000)]
    public int MaxOutputChars { get; set; } = 1_000_000;

    public List<string> PassThroughEnvironment { get; set; } = [.. DefaultPassThroughEnvironment];

    /// <summary>Variáveis fixas a acrescentar ao ambiente do processo (usado pelos testes).</summary>
    public Dictionary<string, string> AdditionalEnvironment { get; set; } = [];
}
