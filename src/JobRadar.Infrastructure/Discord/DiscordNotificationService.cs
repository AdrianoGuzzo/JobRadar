using System.Globalization;
using System.Net;
using Discord;
using Discord.Net;
using Discord.Net.Rest;
using Discord.Rest;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Common;
using JobRadar.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JobRadar.Infrastructure.Discord;

/// <summary>
/// Publica as notificações num canal pelo Discord.Net (só a API REST: o MVP não
/// precisa de gateway). O Discord.Net não faz retry: quem decide é a política de
/// novas tentativas do JobRadar (ADR-002), a partir da classificação daqui.
/// </summary>
public sealed partial class DiscordNotificationService(IOptions<DiscordOptions> options, ILogger<DiscordNotificationService> logger)
    : IDiscordNotificationService, IAsyncDisposable
{
    private readonly DiscordOptions _options = options.Value;

    private readonly SemaphoreSlim _connection = new(1, 1);

    private DiscordRestClient? _client;

    private IMessageChannel? _channel;

    public async Task<string> SendAsync(JobNotificationMessage message, CancellationToken cancellationToken)
    {
        // O RequestOptions.Timeout do Discord.Net não interrompe a chamada HTTP; este token sim.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

        try
        {
            var channel = await GetChannelAsync(timeout.Token);
            var sent = await channel.SendMessageAsync(
                text: DiscordMessageFormatter.Format(message),
                allowedMentions: AllowedMentions.None,
                options: RequestOptions(timeout.Token));

            return sent.Id.ToString(CultureInfo.InvariantCulture);
        }
        catch (NotificationDeliveryException)
        {
            throw;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            var kind = Classify(exception);

            if (kind == FailureKind.Permanent || _channel is null)
            {
                await ResetAsync();
            }

            var description = SecretRedactor.Redact($"Falha ao enviar ao Discord: {exception.Message}", _options.BotToken);
            LogDeliveryFailed(logger, kind, exception.GetType().Name);
            throw new NotificationDeliveryException(kind, description, exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _connection.Dispose();
    }

    /// <summary>Credencial, permissão e canal inexistente não se resolvem sozinhos; o resto sim.</summary>
    internal static FailureKind Classify(Exception exception) => exception switch
    {
        HttpException http when http.HttpCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound
            => FailureKind.Permanent,
        ArgumentException => FailureKind.Permanent,
        _ => FailureKind.Transient,
    };

    private async Task<IMessageChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        await _connection.WaitAsync(cancellationToken);

        try
        {
            if (_channel is not null)
            {
                return _channel;
            }

            var client = new DiscordRestClient(Config());

            try
            {
                await client.LoginAsync(TokenType.Bot, _options.BotToken).WaitAsync(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds), cancellationToken);

                if (await client.GetChannelAsync(_options.ChannelId, RequestOptions(cancellationToken)) is not IMessageChannel channel)
                {
                    throw new NotificationDeliveryException(
                        FailureKind.Permanent, $"O canal {_options.ChannelId} do Discord não existe, não é visível para o bot ou não aceita mensagens.");
                }

                _client = client;
                _channel = channel;
                return channel;
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _connection.Release();
        }
    }

    private DiscordRestConfig Config()
    {
        var config = new DiscordRestConfig { DefaultRetryMode = RetryMode.AlwaysFail };

        if (_options.ApiBaseUrl is { Length: > 0 } baseUrl)
        {
            config.RestClientProvider = _ => DefaultRestClientProvider.Instance(baseUrl);
        }

        return config;
    }

    private RequestOptions RequestOptions(CancellationToken cancellationToken) => new()
    {
        CancelToken = cancellationToken,
        RetryMode = RetryMode.AlwaysFail,
        Timeout = _options.RequestTimeoutSeconds * 1000,
    };

    private async Task ResetAsync()
    {
        var client = _client;
        _client = null;
        _channel = null;

        if (client is not null)
        {
            await client.DisposeAsync();
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Envio ao Discord falhou ({Kind}, {ExceptionType}).")]
    private static partial void LogDeliveryFailed(ILogger logger, FailureKind kind, string exceptionType);
}
