using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.LinkedIn;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class LinkedInEmailJobSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Should_Return_Postings_From_Alerts_With_Mailbox_Checkpoint()
    {
        var mailbox = new FakeMailbox(new MailboxBatch([Load("alerta-html-tres-vagas.eml"), Load("alerta-somente-texto.eml")], "7:42"));

        var batch = await Source(mailbox).FetchAsync("7:40", Ct);

        batch.Postings.Should().HaveCount(4);
        batch.Checkpoint.Should().Be("7:42");
        mailbox.ReceivedCheckpoint.Should().Be("7:40");
    }

    [Fact]
    public async Task Should_Ignore_Messages_Not_Sent_By_LinkedIn()
    {
        var mailbox = new FakeMailbox(new MailboxBatch([Load("nao-linkedin.eml")], "7:43"));

        var batch = await Source(mailbox).FetchAsync(null, Ct);

        batch.Postings.Should().BeEmpty();
        batch.Checkpoint.Should().Be("7:43", "o e-mail foi lido e não precisa ser lido de novo");
    }

    [Fact]
    public async Task Should_Not_Repeat_A_Job_Present_In_Two_Alerts()
    {
        var mailbox = new FakeMailbox(new MailboxBatch([Load("alerta-html-tres-vagas.eml"), Load("alerta-html-tres-vagas.eml")], "7:44"));

        var batch = await Source(mailbox).FetchAsync(null, Ct);

        batch.Postings.Select(p => p.ExternalId).Should().OnlyHaveUniqueItems().And.HaveCount(3);
    }

    [Fact]
    public async Task Should_Identify_Itself_As_LinkedIn()
    {
        Source(new FakeMailbox(new MailboxBatch([], null))).Source.Should().Be(JobSource.LinkedIn);
    }

    private static LinkedInEmailJobSource Source(FakeMailbox mailbox) => new(mailbox, NullLogger<LinkedInEmailJobSource>.Instance);

    private static MimeMessage Load(string name) => LinkedInAlertParserTests.Load(name);

    private sealed class FakeMailbox(MailboxBatch batch) : IAlertMailbox
    {
        public string? ReceivedCheckpoint { get; private set; }

        public Task<MailboxBatch> FetchAsync(string? checkpoint, CancellationToken cancellationToken)
        {
            ReceivedCheckpoint = checkpoint;
            return Task.FromResult(batch);
        }
    }
}
