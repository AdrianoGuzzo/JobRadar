using System.Text.Json;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;

namespace JobRadar.Infrastructure.Claude;

/// <summary>
/// Lê a saída do <c>claude -p --output-format json</c> e valida a análise antes
/// de ela chegar ao domínio (ADR-003). Resposta inválida é falha transitória:
/// uma nova tentativa pode dar certo, e o limite de tentativas encerra o caso.
/// </summary>
public static class ClaudeResponseParser
{
    public const int MaxListItems = 15;

    public const int MaxTextLength = 2000;

    public const int MaxItemLength = 300;

    private static readonly string[] PermanentMarkers =
    [
        "credit balance", "invalid api key", "/login", "oauth", "authentication", "unauthorized", "forbidden", "not logged in",
    ];

    public static JobAnalysis Parse(string stdout)
    {
        var root = ParseJson(stdout, "A saída do Claude não é JSON válido.");

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var type) && type.ValueEquals("result"))
        {
            return ParseEnvelope(root);
        }

        return ToAnalysis(root);
    }

    /// <summary>Erros de autenticação e cobrança não se resolvem sozinhos; o resto (limite, sobrecarga, 5xx) sim.</summary>
    public static FailureKind Classify(string message, int? apiErrorStatus)
    {
        if (apiErrorStatus is 401 or 403)
        {
            return FailureKind.Permanent;
        }

        return PermanentMarkers.Any(marker => message.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? FailureKind.Permanent
            : FailureKind.Transient;
    }

    private static JobAnalysis ParseEnvelope(JsonElement envelope)
    {
        var resultText = envelope.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String ? result.GetString() ?? string.Empty : string.Empty;

        if (envelope.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True)
        {
            int? status = envelope.TryGetProperty("api_error_status", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var value)
                ? value
                : null;
            var message = string.IsNullOrWhiteSpace(resultText) ? "erro sem mensagem" : resultText;
            throw new JobAnalysisException(Classify(message, status), $"O Claude retornou erro: {Shorten(message, MaxItemLength)}");
        }

        if (envelope.TryGetProperty("structured_output", out var structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return ToAnalysis(structured);
        }

        return ToAnalysis(ParseJson(ExtractJsonObject(resultText), "A resposta do Claude não contém um JSON de análise."));
    }

    private static JobAnalysis ToAnalysis(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("a análise não é um objeto JSON");
        }

        try
        {
            return new JobAnalysis(
                Score: new CompatibilityScore(ReadScore(json)),
                JobTitle: ReadOptionalText(json, "jobTitle"),
                Seniority: ReadOptionalText(json, "seniority"),
                Strengths: ReadList(json, "strengths"),
                Gaps: ReadList(json, "gaps"),
                TechnologyMatches: ReadTechnologyMatches(json),
                Recommendation: ReadRecommendation(json),
                Summary: Shorten(ReadOptionalText(json, "summary", MaxTextLength) ?? string.Empty, MaxTextLength),
                MissingInformation: ReadList(json, "missingInformation"));
        }
        catch (DomainException exception)
        {
            throw Invalid(exception.Message);
        }
    }

    private static int ReadScore(JsonElement json)
    {
        if (!json.TryGetProperty("compatibilityScore", out var score) || score.ValueKind != JsonValueKind.Number)
        {
            throw Invalid("\"compatibilityScore\" ausente ou não numérico");
        }

        return score.TryGetInt32(out var integer) ? integer : (int)Math.Round(score.GetDouble());
    }

    private static Recommendation ReadRecommendation(JsonElement json)
    {
        var text = ReadOptionalText(json, "recommendation");

        return text is not null && Enum.TryParse<Recommendation>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : throw Invalid($"\"recommendation\" inválida ({text ?? "ausente"})");
    }

    private static List<TechnologyMatch> ReadTechnologyMatches(JsonElement json)
    {
        if (!json.TryGetProperty("technologyMatches", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => (Technology: ReadOptionalText(item, "technology"), Level: ReadOptionalText(item, "match")))
            .Where(item => item.Technology is not null)
            .Select(item => new TechnologyMatch(
                Shorten(item.Technology!, MaxItemLength),
                Enum.TryParse<TechnologyMatchLevel>(item.Level, ignoreCase: true, out var level) && Enum.IsDefined(level) ? level : TechnologyMatchLevel.NotInformed))
            .Take(MaxListItems)];
    }

    private static List<string> ReadList(JsonElement json, string property)
    {
        if (!json.TryGetProperty(property, out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim())
            .Where(text => text.Length > 0)
            .Select(text => Shorten(text, MaxItemLength))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)];
    }

    private static string? ReadOptionalText(JsonElement json, string property, int maxLength = MaxItemLength) =>
        json.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? Shorten(value.GetString()!.Trim(), maxLength)
            : null;

    private static JsonElement ParseJson(string text, string error)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new JobAnalysisException(FailureKind.Transient, error, exception);
        }
    }

    /// <summary>O primeiro objeto JSON do texto: tolera prosa e blocos de código em volta.</summary>
    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    private static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private static JobAnalysisException Invalid(string reason) =>
        new(FailureKind.Transient, $"Resposta do Claude inválida: {reason}.");
}
