using System.Text.Json;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.Discord;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.Discord;

[Trait("Category", "Integration")]
public sealed class DiscordNotificationServiceTests : IAsyncDisposable
{
    private const string Token = "MTA5ODc2NTQzMjEwOTg3NjU0Mw" + ".GaBcDe.token-de-teste-abcdefghijklmnopqrstuvwx";

    private readonly FakeDiscordServer _server = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [Fact]
    public async Task Should_Send_Review_Buttons_With_Stable_Notification_Id()
    {
        await using var service = Service(interactionsEnabled: true);
        var id = Guid.NewGuid();

        await service.SendAsync(Message() with { NotificationId = id, ReviewStatus = JobReviewStatus.Approved }, Ct);

        using var body = JsonDocument.Parse(_server.Requests.Single(request => request.Method == "POST").Body);
        var buttons = body.RootElement.GetProperty("components")[0].GetProperty("components");
        buttons[0].GetProperty("label").GetString().Should().Be("Gostei");
        buttons[0].GetProperty("custom_id").GetString().Should().Be($"job-review:v1:approve:{id:N}");
        buttons[1].GetProperty("label").GetString().Should().Be("Descartar");
        buttons[2].GetProperty("label").GetString().Should().Be("Aprovada");
        buttons[2].GetProperty("disabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Should_Send_Formatted_Message_To_Configured_Channel()
    {
        await using var service = Service();

        var messageId = await service.SendAsync(Message(), Ct);

        messageId.Should().MatchRegex("^[0-9]+$");
        var post = _server.Requests.Single(request => request.Method == "POST");
        post.Path.Should().EndWith("/channels/222/messages");
        post.Authorization.Should().Be($"Bot {Token}");
        using var body = JsonDocument.Parse(post.Body);
        body.RootElement.GetProperty("content").GetString().Should().Contain("**NOVA OPORTUNIDADE!**").And.Contain("Compatibilidade: **92%**");
    }

    [Fact]
    public async Task Should_Not_Allow_Any_Mention()
    {
        await using var service = Service();

        await service.SendAsync(Message() with { Title = "@everyone vaga" }, Ct);

        using var body = JsonDocument.Parse(_server.Requests.Single(request => request.Method == "POST").Body);
        // Para o Discord, allowed_mentions sem tipos em "parse" e sem ids libera nenhuma menção.
        var mentions = body.RootElement.GetProperty("allowed_mentions");
        var parse = mentions.GetProperty("parse");
        (parse.ValueKind == JsonValueKind.Null || parse.GetArrayLength() == 0).Should().BeTrue();
        mentions.GetProperty("users").GetArrayLength().Should().Be(0);
        mentions.GetProperty("roles").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Should_Log_In_Once_For_Many_Messages()
    {
        await using var service = Service();

        for (var i = 0; i < 3; i++)
        {
            await service.SendAsync(Message(), Ct);
        }

        _server.Count("/users/@me").Should().Be(1);
        _server.Count("/messages").Should().Be(3);
    }

    [Fact]
    public async Task Should_Treat_Rate_Limit_As_Transient()
    {
        _server.Respond("/messages", 429, """{"message":"You are being rate limited.","retry_after":5.0,"global":false}""");
        await using var service = Service();

        var act = () => service.SendAsync(Message(), Ct);

        (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which.Kind.Should().Be(FailureKind.Transient);
        _server.Count("/messages").Should().Be(1, "o retry é do JobRadar, não do Discord.Net");
    }

    [Fact]
    public async Task Should_Treat_Server_Error_As_Transient()
    {
        _server.Respond("/messages", 503, """{"message":"Service Unavailable","code":0}""");
        await using var service = Service();

        var act = () => service.SendAsync(Message(), Ct);

        (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Fact]
    public async Task Should_Treat_Invalid_Token_As_Permanent_And_Not_Leak_It()
    {
        _server.Respond("/users/@me", 401, """{"message":"401: Unauthorized","code":0}""");
        await using var service = Service();

        var act = () => service.SendAsync(Message(), Ct);

        var exception = (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which;
        exception.Kind.Should().Be(FailureKind.Permanent);
        exception.Message.Should().NotContain(Token);
    }

    [Fact]
    public async Task Should_Treat_Missing_Channel_As_Permanent()
    {
        _server.Respond("/channels/222", 404, """{"message":"Unknown Channel","code":10003}""");
        await using var service = Service();

        var act = () => service.SendAsync(Message(), Ct);

        (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which.Kind.Should().Be(FailureKind.Permanent);
    }

    [Fact]
    public async Task Should_Treat_Timeout_As_Transient()
    {
        _server.Respond("/messages", 200, "{}", delay: TimeSpan.FromSeconds(5));
        await using var service = Service(requestTimeoutSeconds: 1);

        var act = () => service.SendAsync(Message(), Ct);

        (await act.Should().ThrowAsync<NotificationDeliveryException>()).Which.Kind.Should().Be(FailureKind.Transient);
    }

    [Fact]
    public async Task Should_Recover_After_Transient_Login_Failure()
    {
        _server.Respond("/users/@me", 503, """{"message":"unavailable","code":0}""");
        await using var service = Service();

        var first = () => service.SendAsync(Message(), Ct);
        await first.Should().ThrowAsync<NotificationDeliveryException>();

        var messageId = await service.SendAsync(Message(), Ct);

        messageId.Should().NotBeNullOrEmpty();
    }

    private DiscordNotificationService Service(int requestTimeoutSeconds = 10, bool interactionsEnabled = false) => new(
        Options.Create(new DiscordOptions { BotToken = Token, ChannelId = 222, ApiBaseUrl = _server.BaseUrl,
            RequestTimeoutSeconds = requestTimeoutSeconds, InteractionsEnabled = interactionsEnabled }),
        NullLogger<DiscordNotificationService>.Instance);

    private static JobNotificationMessage Message() => new(
        Title: "Senior .NET Developer",
        Company: "Agro Tech",
        Score: 92,
        WorkMode: WorkMode.Remote,
        Location: "Brasil",
        SourceName: "LinkedIn",
        Strengths: ["C# / .NET"],
        Gaps: ["Inglês avançado"],
        MissingInformation: [],
        Summary: "Alta compatibilidade.",
        Url: "https://www.linkedin.com/jobs/view/123/");
}
