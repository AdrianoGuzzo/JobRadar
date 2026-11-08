using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using JobRadar.Infrastructure.LinkedIn;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using MimeKit;

namespace JobRadar.IntegrationTests.Email;

/// <summary>Contra um servidor IMAP real (GreenMail) em container. Cada teste usa um usuário novo.</summary>
[Trait("Category", "Integration")]
public sealed class ImapAlertMailboxTests : IAsyncLifetime
{
    private const int ImapPort = 3143;

    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly IContainer _greenMail = new ContainerBuilder("greenmail/standalone:2.1.3")
        .WithEnvironment("GREENMAIL_OPTS", "-Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled -Dgreenmail.verbose=false")
        .WithPortBinding(ImapPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(ImapPort))
        .Build();

    private readonly string _user = $"alertas-{Guid.NewGuid():N}@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _greenMail.StartAsync(Ct);

    public async ValueTask DisposeAsync() => await _greenMail.DisposeAsync();

    [Fact]
    public async Task Should_Fetch_Messages_Within_Lookback_On_First_Run()
    {
        await Deliver(Alert("antigo"), Now.AddDays(-30));
        await Deliver(Alert("recente-1"), Now.AddDays(-1));
        await Deliver(Alert("recente-2"), Now.AddHours(-1));

        var batch = await Mailbox().FetchAsync(null, Ct);

        batch.Messages.Select(m => m.Subject).Should().Equal("recente-1", "recente-2");
        batch.Checkpoint.Should().MatchRegex(@"^\d+:3$");
    }

    [Fact]
    public async Task Should_Fetch_Only_New_Messages_After_Checkpoint()
    {
        await Deliver(Alert("primeiro"), Now.AddHours(-2));
        var first = await Mailbox().FetchAsync(null, Ct);

        await Deliver(Alert("segundo"), Now.AddHours(-1));
        var second = await Mailbox().FetchAsync(first.Checkpoint, Ct);
        var third = await Mailbox().FetchAsync(second.Checkpoint, Ct);

        second.Messages.Select(m => m.Subject).Should().Equal("segundo");
        third.Messages.Should().BeEmpty();
        third.Checkpoint.Should().Be(second.Checkpoint);
    }

    [Fact]
    public async Task Should_Not_Mark_Messages_As_Read()
    {
        await Deliver(Alert("não lido"), Now.AddHours(-1));

        await Mailbox().FetchAsync(null, Ct);

        using var client = await Connect();
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, Ct);
        var summaries = await client.Inbox.FetchAsync(0, -1, MessageSummaryItems.Flags, Ct);
        summaries.Should().ContainSingle().Which.Flags!.Value.HasFlag(MessageFlags.Seen).Should().BeFalse();
    }

    [Fact]
    public async Task Should_Start_Over_When_UidValidity_Changes()
    {
        await Deliver(Alert("único"), Now.AddHours(-1));

        var batch = await Mailbox().FetchAsync("1:999", Ct);

        batch.Messages.Select(m => m.Subject).Should().Equal("único");
    }

    [Fact]
    public async Task Should_Limit_Messages_Per_Run_And_Continue_Next_Time()
    {
        await Deliver(Alert("a"), Now.AddHours(-3));
        await Deliver(Alert("b"), Now.AddHours(-2));
        await Deliver(Alert("c"), Now.AddHours(-1));

        var first = await Mailbox(maxMessages: 2).FetchAsync(null, Ct);
        var second = await Mailbox(maxMessages: 2).FetchAsync(first.Checkpoint, Ct);

        first.Messages.Select(m => m.Subject).Should().Equal("a", "b");
        second.Messages.Select(m => m.Subject).Should().Equal("c");
    }

    [Fact]
    public async Task Should_Report_Missing_Folder_Clearly()
    {
        var act = () => Mailbox(folder: "NaoExiste").FetchAsync(null, Ct);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("NaoExiste").And.Contain("Imap__Folder");
    }

    private ImapAlertMailbox Mailbox(int maxMessages = 50, string folder = "INBOX") => new(
        Options.Create(new ImapOptions
        {
            Host = _greenMail.Hostname,
            Port = _greenMail.GetMappedPublicPort(ImapPort),
            Security = SecureSocketOptions.None,
            User = _user,
            Password = "senha-de-teste",
            Folder = folder,
            LookbackDays = 7,
            MaxMessagesPerRun = maxMessages,
        }),
        new FakeTimeProvider(Now),
        NullLogger<ImapAlertMailbox>.Instance);

    private async Task<ImapClient> Connect()
    {
        var client = new ImapClient();
        await client.ConnectAsync(_greenMail.Hostname, _greenMail.GetMappedPublicPort(ImapPort), SecureSocketOptions.None, Ct);
        await client.AuthenticateAsync(_user, "senha-de-teste", Ct);
        return client;
    }

    private async Task Deliver(MimeMessage message, DateTimeOffset receivedAt)
    {
        using var client = await Connect();
        await client.Inbox.AppendAsync(new AppendRequest(message, MessageFlags.None) { InternalDate = receivedAt }, Ct);
        await client.DisconnectAsync(true, Ct);
    }

    private static MimeMessage Alert(string subject)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("LinkedIn", "jobalerts-noreply@linkedin.com"));
        message.To.Add(new MailboxAddress("Candidato", "candidato@example.com"));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = "Vaga\nEmpresa\nhttps://www.linkedin.com/jobs/view/4000000001/" };
        return message;
    }
}
