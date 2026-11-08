using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace JobRadar.Infrastructure.Processes;

public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string StandardInput,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    TimeSpan Timeout,
    int MaxOutputChars);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

public sealed class ProcessStartException(string message, Exception innerException) : Exception(message, innerException);

public sealed class ProcessOutputTooLargeException(string message) : Exception(message);

/// <summary>
/// Executa um processo sem shell: argumentos só por <see cref="ProcessStartInfo.ArgumentList"/>,
/// ambiente substituído por inteiro, entrada pelo stdin, saída com limite de
/// tamanho, e a árvore inteira morta em timeout ou cancelamento (ADR-003).
/// </summary>
public static class ProcessRunner
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <exception cref="TimeoutException">O processo passou de <see cref="ProcessRequest.Timeout"/>.</exception>
    /// <exception cref="OperationCanceledException">O chamador cancelou.</exception>
    /// <exception cref="ProcessStartException">O executável não pôde ser iniciado.</exception>
    /// <exception cref="ProcessOutputTooLargeException">A saída passou do limite.</exception>
    public static async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            WorkingDirectory = request.WorkingDirectory,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();

        foreach (var (name, value) in request.Environment)
        {
            startInfo.Environment[name] = value;
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            throw new ProcessStartException($"Não foi possível iniciar \"{request.FileName}\": {exception.Message}", exception);
        }

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var stdout = ReadLimitedAsync(process.StandardOutput, request.MaxOutputChars, linked.Token);
        var stderr = ReadLimitedAsync(process.StandardError, request.MaxOutputChars, linked.Token);

        try
        {
            await WriteInputAsync(process, request.StandardInput, linked.Token);
            await process.WaitForExitAsync(linked.Token);
            return new ProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            await Observe(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"O processo excedeu o tempo limite de {request.Timeout.TotalSeconds:0} s.");
        }
        catch (ProcessOutputTooLargeException)
        {
            KillTree(process);
            await Observe(stdout, stderr);
            throw;
        }
    }

    private static async Task WriteInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // O processo fechou o stdin antes de ler tudo; o código de saída conta a história.
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var builder = new StringBuilder();

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);

            if (read == 0)
            {
                return builder.ToString();
            }

            if (builder.Length + read > maxChars)
            {
                throw new ProcessOutputTooLargeException($"A saída do processo passou do limite de {maxChars} caracteres.");
            }

            builder.Append(buffer, 0, read);
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (InvalidOperationException)
        {
            // Já tinha terminado.
        }
    }

    private static async Task Observe(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (exception is OperationCanceledException or ProcessOutputTooLargeException or IOException or TimeoutException)
        {
            // Leituras interrompidas de propósito.
        }
    }
}
