using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace JobRadar.FakeClaude;

/// <summary>
/// Imita o <c>claude -p --output-format json</c>. O cenário vem de
/// <c>FAKE_CLAUDE_SCENARIO</c>; arquivos de estado vão para
/// <c>FAKE_CLAUDE_STATE_DIR</c>, que os testes inspecionam.
/// </summary>
internal static class Program
{
    private const string ValidAnalysis = """
        {"compatibilityScore":92,"jobTitle":"Senior .NET Developer","seniority":"Senior",
         "strengths":["Experiência com C# e .NET"],"gaps":["Inglês avançado exigido"],
         "technologyMatches":[{"technology":".NET","match":"Excellent"}],
         "recommendation":"HighMatch","summary":"Alta compatibilidade com o perfil profissional.","missingInformation":[]}
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--child"])
        {
            await Task.Delay(Timeout.Infinite);
            return 0;
        }

        if (args is ["--version"])
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("FAKE_CLAUDE_VERSION") ?? "2.1.319 (Claude Code)");
            return 0;
        }

        Console.OutputEncoding = new UTF8Encoding(false);
        Console.InputEncoding = new UTF8Encoding(false);
        var stdin = await Console.In.ReadToEndAsync();
        var stateDir = Environment.GetEnvironmentVariable("FAKE_CLAUDE_STATE_DIR");
        var scenario = Environment.GetEnvironmentVariable("FAKE_CLAUDE_SCENARIO") ?? "valid";

        switch (scenario)
        {
            case "valid":
                Console.Write(Envelope(result: ValidAnalysis));
                return 0;

            case "structured":
                Console.Write(JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = false, result = "", structured_output = JsonDocument.Parse(ValidAnalysis).RootElement }));
                return 0;

            case "invalid-json":
                Console.Write(Envelope(result: "Desculpe, não consigo responder em JSON."));
                return 0;

            case "score-out-of-range":
                Console.Write(Envelope(result: ValidAnalysis.Replace("\"compatibilityScore\":92", "\"compatibilityScore\":150", StringComparison.Ordinal)));
                return 0;

            case "error-auth":
                Console.Write(JsonSerializer.Serialize(new { type = "result", is_error = true, api_error_status = 401, result = "Invalid API key · Please run /login" }));
                return 1;

            case "error-rate-limit":
                Console.Write(JsonSerializer.Serialize(new { type = "result", is_error = true, api_error_status = 429, result = "Rate limit exceeded" }));
                return 1;

            case "crash":
                Console.Error.Write("panic: segredo sk-ant-oat01-abcdefghijklmnopqrstuvwxyz vazou no stderr");
                return 3;

            case "hang":
                await Task.Delay(Timeout.Infinite);
                return 0;

            case "spawn-child":
                using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--child") { UseShellExecute = false }))
                {
                    await File.WriteAllTextAsync(Path.Combine(stateDir!, "child.pid"), child!.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                await Task.Delay(Timeout.Infinite);
                return 0;

            case "slow":
                var marker = Path.Combine(stateDir!, $"running-{Environment.ProcessId}");
                await File.WriteAllTextAsync(marker, string.Empty);
                await Task.Delay(400);
                File.Delete(marker);
                Console.Write(Envelope(result: ValidAnalysis));
                return 0;

            case "echo":
                var invocation = new
                {
                    args,
                    stdin,
                    cwd = Environment.CurrentDirectory,
                    env = Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(key => key).ToArray(),
                };
                await File.WriteAllTextAsync(Path.Combine(stateDir!, "invocation.json"), JsonSerializer.Serialize(invocation));
                Console.Write(Envelope(result: ValidAnalysis));
                return 0;

            case "huge-output":
                Console.Write(new string('x', 5_000_000));
                return 0;

            default:
                Console.Error.Write($"cenário desconhecido: {scenario}");
                return 64;
        }
    }

    private static string Envelope(string result) =>
        JsonSerializer.Serialize(new { type = "result", subtype = "success", is_error = false, result });
}
