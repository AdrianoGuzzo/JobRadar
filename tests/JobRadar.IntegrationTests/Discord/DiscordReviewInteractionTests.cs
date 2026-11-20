using JobRadar.Domain.Jobs;
using JobRadar.Worker.Jobs;

namespace JobRadar.IntegrationTests.Discord;

[Trait("Category", "Integration")]
public sealed class DiscordReviewInteractionTests
{
    [Fact]
    public async Task Should_Await_Acknowledgement_Before_Accessing_Persistence()
    {
        var ack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processed = false;
        var task = DiscordReviewInteraction.RunAsync(_ => ack.Task, _ =>
        {
            processed = true;
            return Task.CompletedTask;
        },
        (_, _) => throw new InvalidOperationException("Unexpected reply."),
        (_, _) => throw new InvalidOperationException("Unexpected error."),
        TestContext.Current.CancellationToken);

        processed.Should().BeFalse();
        ack.SetResult();
        await task;
        processed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_Reply_To_A_Database_Error_Only_After_Acknowledgement(bool acknowledged)
    {
        var events = new List<string>();
        await DiscordReviewInteraction.RunAsync(
            _ => acknowledged ? Task.CompletedTask : Task.FromException(new TimeoutException()),
            async _ =>
            {
                events.Add("database");
                await DiscordReviewInteraction.ApplyAsync(
                    () => Task.FromException<JobReviewStatus?>(new InvalidOperationException()),
                    _ => { events.Add("edit"); return Task.CompletedTask; },
                    (_, _) => events.Add("edit error"));
            },
            (message, _) => { events.Add(message); return Task.CompletedTask; },
            (stage, _) => events.Add(stage),
            TestContext.Current.CancellationToken);

        if (acknowledged)
        {
            events.Should().Equal("database", "interação", "Não foi possível concluir a ação. Tente novamente.");
        }
        else
        {
            events.Should().Equal("interação");
        }
    }

    [Fact]
    public async Task Should_Confirm_Saved_Decision_When_Message_Edit_Fails()
    {
        var events = new List<string>();
        var confirmation = await DiscordReviewInteraction.ApplyAsync(
            () =>
            {
                events.Add("database");
                return Task.FromResult<JobReviewStatus?>(JobReviewStatus.Approved);
            },
            status =>
            {
                status.Should().Be(JobReviewStatus.Approved);
                events.Add("edit");
                return Task.FromException(new TimeoutException());
            },
            (stage, _) => events.Add(stage));

        events.Should().Equal("database", "edit", "edição da mensagem");
        confirmation.Should().Be("Decisão registrada: Aprovada. Não foi possível atualizar a mensagem.");
    }
}
