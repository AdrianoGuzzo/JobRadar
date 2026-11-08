using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.External;

/// <summary>
/// Publica uma mensagem de teste num canal real. Só roda com
/// <c>JOBRADAR_EXTERNAL_DISCORD_TOKEN</c> e <c>JOBRADAR_EXTERNAL_DISCORD_CHANNEL</c>.
/// </summary>
[Trait("Category", "External")]
public sealed class RealDiscordTests
{
    [Fact]
    public async Task Should_Post_A_Test_Message_To_The_Real_Channel()
    {
        var token = Environment.GetEnvironmentVariable("JOBRADAR_EXTERNAL_DISCORD_TOKEN");
        var channel = Environment.GetEnvironmentVariable("JOBRADAR_EXTERNAL_DISCORD_CHANNEL");
        Assert.SkipWhen(string.IsNullOrEmpty(token) || string.IsNullOrEmpty(channel), "Defina JOBRADAR_EXTERNAL_DISCORD_TOKEN e JOBRADAR_EXTERNAL_DISCORD_CHANNEL.");

        await using var service = new DiscordNotificationService(
            Options.Create(new DiscordOptions { BotToken = token!, ChannelId = ulong.Parse(channel!, System.Globalization.CultureInfo.InvariantCulture) }),
            NullLogger<DiscordNotificationService>.Instance);

        var id = await service.SendAsync(
            new JobNotificationMessage(
                "[TESTE] JobRadar", null, 100, WorkMode.Remote, "Brasil", "Teste", ["Integração funcionando"], [], [], "Mensagem de teste do JobRadar.", "https://example.com"),
            TestContext.Current.CancellationToken);

        id.Should().NotBeNullOrEmpty();
    }
}
