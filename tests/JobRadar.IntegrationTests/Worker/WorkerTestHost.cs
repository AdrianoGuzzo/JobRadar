using JobRadar.Worker.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobRadar.IntegrationTests.Worker;

/// <summary>A composição real do Worker, apontada para dublês locais (Postgres de teste, FakeClaude, Discord falso).</summary>
internal static class WorkerTestHost
{
    public static readonly string FakeClaude =
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "JobRadar.FakeClaude.exe" : "JobRadar.FakeClaude");

    public static readonly string ExampleProfile = Path.Combine(AppContext.BaseDirectory, "Fixtures", "profile.example.json");

    public static Dictionary<string, string?> Settings(string connectionString = "Host=localhost;Database=nao-usado;Username=u;Password=p", string? discordBaseUrl = null) => new()
    {
        ["ConnectionStrings:JobRadar"] = connectionString,
        ["Claude:ExecutablePath"] = FakeClaude,
        ["Claude:AdditionalEnvironment:FAKE_CLAUDE_SCENARIO"] = "valid",
        ["Discord:BotToken"] = "MTA5ODc2NTQzMjEwOTg3NjU0Mw" + ".GaBcDe.token-de-teste-abcdefghijklmnopqrstuvwx",
        ["Discord:ChannelId"] = "222",
        ["Discord:ApiBaseUrl"] = discordBaseUrl,
        ["Imap:Host"] = "imap.example.com",
        ["Imap:User"] = "alertas@example.com",
        ["Imap:Password"] = "senha-de-app",
        ["Profile:Path"] = ExampleProfile,
    };

    public static ServiceProvider Build(Dictionary<string, string?> settings, Action<IServiceCollection>? replace = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddJobRadarCore(configuration);
        replace?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
