using System.Globalization;
using Discord;
using Discord.WebSocket;
using JobRadar.Application.Jobs;
using JobRadar.Infrastructure.Discord;
using Microsoft.Extensions.Options;

namespace JobRadar.Worker.Jobs;

internal sealed partial class DiscordInteractionWorker(
    IOptions<DiscordOptions> configuration, ReviewJobs reviews, ILogger<DiscordInteractionWorker> logger) : BackgroundService
{
    private const string CommandName = "vagas-aprovadas";
    private readonly DiscordOptions _options = configuration.Value;
    // Interactions are acknowledged before entering this gate. It orders writes and message edits.
    private readonly SemaphoreSlim _edits = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.InteractionsEnabled)
        {
            return;
        }

        _options.TryGetAllowedUsers(out var allowedUsers);
        await using var client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            DefaultRetryMode = RetryMode.AlwaysFail,
        });
        client.ButtonExecuted += interaction =>
        {
            if (interaction.Data.CustomId.StartsWith("job-review:", StringComparison.Ordinal))
            {
                _ = Task.Run(() => HandleAsync(interaction, client.CurrentUser.Id, allowedUsers, stoppingToken), CancellationToken.None);
            }

            return Task.CompletedTask;
        };
        client.SlashCommandExecuted += interaction =>
        {
            if (interaction.CommandName == CommandName)
            {
                _ = Task.Run(() => HandleAsync(interaction, client.CurrentUser.Id, allowedUsers, stoppingToken), CancellationToken.None);
            }

            return Task.CompletedTask;
        };

        try
        {
            await client.LoginAsync(TokenType.Bot, _options.BotToken);
            await client.StartAsync();
            await RegisterCommandAsync(client, stoppingToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogFailure(logger, "gateway", exception.GetType().Name);
        }
    }

    private async Task RegisterCommandAsync(DiscordSocketClient client, CancellationToken stoppingToken)
    {
        var command = new SlashCommandBuilder().WithName(CommandName)
            .WithDescription("Consulta as vagas aprovadas do JobRadar.")
            .AddOption("pagina", ApplicationCommandOptionType.Integer, "Número da página",
                minValue: 1, maxValue: ReviewJobs.MaxPage).Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (client.GetGuild(_options.GuildId) is { } guild)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await guild.CreateApplicationCommandAsync(command, Request(timeout.Token));
                    return;
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogFailure(logger, "registro do comando", exception.GetType().Name);
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private Task HandleAsync(SocketInteraction interaction, ulong botId, IReadOnlySet<ulong> allowedUsers, CancellationToken stoppingToken) =>
        DiscordReviewInteraction.RunAsync(
            token => interaction is SocketMessageComponent component
                ? component.DeferLoadingAsync(ephemeral: true, Request(token))
                : interaction.DeferAsync(ephemeral: true, Request(token)),
            async token =>
            {
                if (interaction.GuildId != _options.GuildId || !reviews.IsAuthorized(interaction.User.Id, allowedUsers))
                {
                    await ReplyAsync(interaction, "Você não tem autorização para avaliar ou consultar vagas.", token);
                    return;
                }

                if (interaction is SocketMessageComponent button)
                {
                    await ApplyAsync(button, botId, allowedUsers, token);
                }
                else if (interaction is SocketSlashCommand command)
                {
                    await ListAsync(command, allowedUsers, token);
                }
            },
            (text, token) => ReplyAsync(interaction, text, token),
            ReportError,
            stoppingToken);

    private void ReportError(string stage, Exception exception) => LogFailure(logger, stage, exception.GetType().Name);

    private async Task ApplyAsync(SocketMessageComponent button, ulong botId, IReadOnlySet<ulong> allowedUsers, CancellationToken token)
    {
        if (button.ChannelId != _options.ChannelId || button.Message.Author.Id != botId
            || !JobReviewComponents.TryParse(button.Data.CustomId, out var notificationId, out var decision))
        {
            await ReplyAsync(button, "Este botão não está disponível para avaliação.", token);
            return;
        }

        string confirmation;
        await _edits.WaitAsync(token);
        try
        {
            confirmation = await DiscordReviewInteraction.ApplyAsync(
                () => reviews.DecideAsync(notificationId, button.Message.Id.ToString(CultureInfo.InvariantCulture),
                    decision, button.User.Id, allowedUsers, token),
                status => button.Message.ModifyAsync(message => message.Components = JobReviewComponents.Build(notificationId, status), Request(token)),
                ReportError);
        }

        finally
        {
            _edits.Release();
        }

        using var reply = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await ReplyAsync(button, confirmation, reply.Token);
    }

    private async Task ListAsync(SocketSlashCommand command, IReadOnlySet<ulong> allowedUsers, CancellationToken token)
    {
        var page = command.Data.Options.FirstOrDefault()?.Value is long value ? value : 1;
        if (page is < 1 or > ReviewJobs.MaxPage)
        {
            await ReplyAsync(command, "O número da página é inválido.", token);
            return;
        }

        var jobs = await reviews.ListAsync((int)page, command.User.Id, allowedUsers, token);
        var embeds = jobs.Select(job => new EmbedBuilder()
            .WithTitle(ApprovedTitle(job.Title))
            .WithUrl(job.Url)
            .WithDescription(string.IsNullOrEmpty(job.Company) ? null : DiscordMessageFormatter.Escape(job.Company))
            .Build()).ToArray();
        var content = jobs.Count == 0
            ? page == 1 ? "Nenhuma vaga aprovada." : "Não há vagas aprovadas nesta página."
            : $"Vagas aprovadas · página {page}";

        await command.ModifyOriginalResponseAsync(message =>
        {
            message.Content = content;
            message.Embeds = embeds;
            message.AllowedMentions = AllowedMentions.None;
        }, Request(token));
    }

    private static string ApprovedTitle(string title)
    {
        var escaped = DiscordMessageFormatter.Escape(title);
        return escaped.Length > 256 ? escaped[..255] + "…" : escaped;
    }

    private static Task ReplyAsync(SocketInteraction interaction, string text, CancellationToken token) =>
        interaction.ModifyOriginalResponseAsync(message =>
        {
            message.Content = text;
            message.AllowedMentions = AllowedMentions.None;
        }, Request(token));

    private static RequestOptions Request(CancellationToken token) => new() { CancelToken = token, RetryMode = RetryMode.AlwaysFail };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Discord: falha na etapa {Stage} ({ErrorType}).")]
    private static partial void LogFailure(ILogger logger, string stage, string errorType);
}
