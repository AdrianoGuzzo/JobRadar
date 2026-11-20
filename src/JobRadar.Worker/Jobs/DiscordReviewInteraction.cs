using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.Discord;

namespace JobRadar.Worker.Jobs;

internal static class DiscordReviewInteraction
{
    public static async Task RunAsync(
        Func<CancellationToken, Task> acknowledge,
        Func<CancellationToken, Task> process,
        Func<string, CancellationToken, Task> reply,
        Action<string, Exception> reportError,
        CancellationToken stoppingToken)
    {
        var acknowledged = false;
        try
        {
            using var acknowledgement = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            acknowledgement.CancelAfter(TimeSpan.FromMilliseconds(2400));
            await acknowledge(acknowledgement.Token);
            acknowledged = true;

            using var work = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            work.CancelAfter(TimeSpan.FromSeconds(15));
            await process(work.Token);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            reportError("interação", exception);
            if (acknowledged)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await reply("Não foi possível concluir a ação. Tente novamente.", timeout.Token);
                }
                catch (Exception replyError)
                {
                    reportError("resposta da interação", replyError);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public static async Task<string> ApplyAsync(
        Func<Task<JobReviewStatus?>> decide, Func<JobReviewStatus, Task> edit, Action<string, Exception> reportError)
    {
        var status = await decide();
        if (status is null)
        {
            return "Vaga ou notificação indisponível. Se o alerta acabou de chegar, tente novamente.";
        }

        var confirmation = $"Decisão registrada: {JobReviewComponents.Label(status.Value)}.";
        try
        {
            await edit(status.Value);
        }
        catch (Exception exception)
        {
            reportError("edição da mensagem", exception);
            confirmation += " Não foi possível atualizar a mensagem.";
        }

        return confirmation;
    }
}
