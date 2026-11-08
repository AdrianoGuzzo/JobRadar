using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JobRadar.IntegrationTests.Discord;

/// <summary>
/// Um Discord de mentira em 127.0.0.1: responde ao login, ao canal e ao envio
/// de mensagens, e deixa cada teste programar falhas por rota.
/// </summary>
internal sealed class FakeDiscordServer : IAsyncDisposable
{
    private const string Me = """{"id":"111","username":"jobradar","discriminator":"0","avatar":null,"bot":true}""";

    private const string Application = """{"id":"111","name":"JobRadar","icon":null,"description":"","bot_public":false,"bot_require_code_grant":false,"flags":0}""";

    private const string Channel = """{"id":"222","type":0,"guild_id":"333","name":"vagas","position":0}""";

    private readonly HttpListener _listener = new();

    private readonly CancellationTokenSource _stop = new();

    private readonly Task _loop;

    public FakeDiscordServer()
    {
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}/api/v10/";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    public string BaseUrl { get; }

    public ConcurrentQueue<(string Method, string Path, string Authorization, string Body)> Requests { get; } = new();

    /// <summary>Respostas programadas por trecho do caminho, consumidas em ordem.</summary>
    public ConcurrentDictionary<string, ConcurrentQueue<(int Status, string Body, TimeSpan Delay)>> Overrides { get; } = new();

    public FakeDiscordServer Respond(string pathFragment, int status, string body = "{}", TimeSpan delay = default)
    {
        Overrides.GetOrAdd(pathFragment, _ => new ConcurrentQueue<(int, string, TimeSpan)>()).Enqueue((status, body, delay));
        return this;
    }

    public int Count(string pathFragment) => Requests.Count(request => request.Path.Contains(pathFragment, StringComparison.Ordinal));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _loop;
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var path = context.Request.Url!.AbsolutePath;
            Requests.Enqueue((context.Request.HttpMethod, path, context.Request.Headers["Authorization"] ?? string.Empty, body));

            var (status, response, delay) = Programmed(path) ?? Default(context.Request.HttpMethod, path);

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            var bytes = Encoding.UTF8.GetBytes(response);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";

            if (status == 429)
            {
                context.Response.Headers["Retry-After"] = "5";
                context.Response.Headers["X-RateLimit-Scope"] = "user";
            }

            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or IOException)
        {
            // O cliente desistiu (timeout do teste).
        }
    }

    private (int, string, TimeSpan)? Programmed(string path)
    {
        foreach (var (fragment, queue) in Overrides)
        {
            if (path.Contains(fragment, StringComparison.Ordinal) && queue.TryDequeue(out var programmed))
            {
                return programmed;
            }
        }

        return null;
    }

    private static (int, string, TimeSpan) Default(string method, string path) => path switch
    {
        _ when path.EndsWith("/users/@me", StringComparison.Ordinal) => (200, Me, TimeSpan.Zero),
        _ when path.EndsWith("/applications/@me", StringComparison.Ordinal) => (200, Application, TimeSpan.Zero),
        _ when path.EndsWith("/messages", StringComparison.Ordinal) && method == "POST" => (200, MessageJson(), TimeSpan.Zero),
        _ when path.Contains("/channels/", StringComparison.Ordinal) => (200, Channel, TimeSpan.Zero),
        _ => (404, """{"message":"Unknown","code":0}""", TimeSpan.Zero),
    };

    private static string MessageJson() =>
        $$"""{"id":"{{Random.Shared.NextInt64(1_000_000_000_000, 9_000_000_000_000)}}","channel_id":"222","type":0,"content":"x","author":{"id":"111","username":"jobradar","discriminator":"0","avatar":null},"timestamp":"2026-11-08T12:00:00+00:00","tts":false,"mention_everyone":false,"mentions":[],"mention_roles":[],"attachments":[],"embeds":[],"pinned":false}""";

    private static int FreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }
}
