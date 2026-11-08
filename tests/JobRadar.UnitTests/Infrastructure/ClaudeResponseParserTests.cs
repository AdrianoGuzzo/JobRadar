using System.Text.Json;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;
using JobRadar.Infrastructure.Claude;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class ClaudeResponseParserTests
{
    private const string ValidAnalysis = """
        {
          "compatibilityScore": 92,
          "jobTitle": "Senior .NET Developer",
          "seniority": "Senior",
          "strengths": ["Experiência com C# e .NET", "APIs REST"],
          "gaps": ["Inglês avançado exigido"],
          "technologyMatches": [{ "technology": ".NET", "match": "Excellent" }, { "technology": "Go", "match": "Missing" }],
          "recommendation": "HighMatch",
          "summary": "Alta compatibilidade com o perfil profissional.",
          "missingInformation": ["faixa salarial"]
        }
        """;

    [Fact]
    public void Should_Parse_Structured_Output_From_Envelope()
    {
        var analysis = ClaudeResponseParser.Parse(Envelope(structuredOutput: ValidAnalysis));

        analysis.Score.Value.Should().Be(92);
        analysis.JobTitle.Should().Be("Senior .NET Developer");
        analysis.Strengths.Should().Equal("Experiência com C# e .NET", "APIs REST");
        analysis.Gaps.Should().Equal("Inglês avançado exigido");
        analysis.TechnologyMatches.Should().Equal(
            new TechnologyMatch(".NET", TechnologyMatchLevel.Excellent), new TechnologyMatch("Go", TechnologyMatchLevel.Missing));
        analysis.Recommendation.Should().Be(Recommendation.HighMatch);
        analysis.MissingInformation.Should().Equal("faixa salarial");
    }

    [Fact]
    public void Should_Parse_Json_From_Result_Text()
    {
        var analysis = ClaudeResponseParser.Parse(Envelope(result: ValidAnalysis));

        analysis.Score.Value.Should().Be(92);
    }

    [Fact]
    public void Should_Parse_Json_Inside_Markdown_Fence()
    {
        var analysis = ClaudeResponseParser.Parse(Envelope(result: $"Segue a análise:\n```json\n{ValidAnalysis}\n```"));

        analysis.Summary.Should().StartWith("Alta compatibilidade");
    }

    [Theory]
    [InlineData("\"compatibilityScore\": 92", "\"compatibilityScore\": 150")]
    [InlineData("\"compatibilityScore\": 92", "\"compatibilityScore\": -5")]
    [InlineData("\"compatibilityScore\": 92", "\"compatibilityScore\": \"alto\"")]
    [InlineData("\"recommendation\": \"HighMatch\"", "\"recommendation\": \"ApplyNow\"")]
    [InlineData("\"summary\": \"Alta compatibilidade com o perfil profissional.\"", "\"summary\": \"\"")]
    [InlineData("\"compatibilityScore\": 92,", "")]
    public void Should_Reject_Invalid_Analysis(string original, string replacement)
    {
        var invalid = ValidAnalysis.Replace(original, replacement, StringComparison.Ordinal);

        var act = () => ClaudeResponseParser.Parse(Envelope(result: invalid));

        act.Should().Throw<JobAnalysisException>().Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("{\"type\":\"result\"")]
    public void Should_Reject_Unreadable_Output(string stdout)
    {
        var act = () => ClaudeResponseParser.Parse(stdout);

        act.Should().Throw<JobAnalysisException>().Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Fact]
    public void Should_Map_Unknown_Technology_Level_To_Not_Informed()
    {
        var analysis = ClaudeResponseParser.Parse(Envelope(result: ValidAnalysis.Replace("\"Missing\"", "\"Talvez\"", StringComparison.Ordinal)));

        analysis.TechnologyMatches[1].Match.Should().Be(TechnologyMatchLevel.NotInformed);
    }

    [Fact]
    public void Should_Limit_List_Sizes_And_Text_Length()
    {
        var strengths = string.Join(',', Enumerable.Range(0, 50).Select(i => $"\"ponto {i}\""));
        var longSummary = new string('a', 10_000);
        var json = ValidAnalysis
            .Replace("\"Experiência com C# e .NET\", \"APIs REST\"", strengths, StringComparison.Ordinal)
            .Replace("Alta compatibilidade com o perfil profissional.", longSummary, StringComparison.Ordinal);

        var analysis = ClaudeResponseParser.Parse(Envelope(result: json));

        analysis.Strengths.Should().HaveCount(ClaudeResponseParser.MaxListItems);
        analysis.Summary.Length.Should().BeLessThanOrEqualTo(ClaudeResponseParser.MaxTextLength);
    }

    [Theory]
    [InlineData("Credit balance is too low", 400, FailureKind.Permanent)]
    [InlineData("Invalid API key · Please run /login", 401, FailureKind.Permanent)]
    [InlineData("OAuth token has expired", 401, FailureKind.Permanent)]
    [InlineData("Claude AI usage limit reached|1762620000", null, FailureKind.Transient)]
    [InlineData("Rate limit exceeded", 429, FailureKind.Transient)]
    [InlineData("Overloaded", 529, FailureKind.Transient)]
    [InlineData("Internal server error", 500, FailureKind.Transient)]
    public void Should_Classify_Error_Envelope(string message, int? status, FailureKind expected)
    {
        var envelope = JsonSerializer.Serialize(new { type = "result", is_error = true, result = message, api_error_status = status });

        var act = () => ClaudeResponseParser.Parse(envelope);

        var exception = act.Should().Throw<JobAnalysisException>().Which;
        exception.Kind.Should().Be(expected);
        exception.Message.Should().Contain(message.Split('|')[0]);
    }

    private static string Envelope(string? result = null, string? structuredOutput = null)
    {
        var envelope = new Dictionary<string, object?>
        {
            ["type"] = "result",
            ["subtype"] = "success",
            ["is_error"] = false,
            ["result"] = result ?? string.Empty,
        };

        if (structuredOutput is not null)
        {
            envelope["structured_output"] = JsonDocument.Parse(structuredOutput).RootElement;
        }

        return JsonSerializer.Serialize(envelope);
    }
}
