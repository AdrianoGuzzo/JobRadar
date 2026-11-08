using System.Text;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;

namespace JobRadar.Infrastructure.Discord;

/// <summary>
/// O texto da notificação no Discord, no formato do escopo. Todo dado externo
/// (vaga e análise) tem markdown e menções neutralizados, e a mensagem nunca
/// passa do limite do Discord, sempre terminando com o link original.
/// </summary>
public static class DiscordMessageFormatter
{
    public const int MaxLength = 2000;

    private const int MaxItems = 8;

    private const int MaxItemLength = 200;

    private const int MaxSummaryLength = 400;

    public static string Format(JobNotificationMessage message)
    {
        var body = new StringBuilder();
        body.Append("**NOVA OPORTUNIDADE!**\n\n");
        body.Append("**").Append(Escape(message.Title)).Append("**");

        if (message.Company is { Length: > 0 } company)
        {
            body.Append(" — ").Append(Escape(company));
        }

        body.Append("\n\nCompatibilidade: **").Append(message.Score).Append("%**\n\n");
        body.Append("Modalidade: ").Append(Describe(message.WorkMode)).Append('\n');
        body.Append("Localização: ").Append(message.Location is { Length: > 0 } location ? Escape(location) : "não informada").Append('\n');
        body.Append("Origem: ").Append(Escape(message.SourceName)).Append('\n');

        AppendList(body, "Pontos fortes", message.Strengths);
        AppendList(body, "Pontos de atenção", message.Gaps);

        if (message.MissingInformation.Count > 0)
        {
            body.Append("\nInformações não disponíveis: ").Append(string.Join(", ", message.MissingInformation.Select(Escape))).Append('\n');
        }

        if (!string.IsNullOrWhiteSpace(message.Summary))
        {
            body.Append("\nResumo: ").Append(Escape(Shorten(message.Summary, MaxSummaryLength))).Append('\n');
        }

        var link = $"\nLink: {message.Url}";
        var budget = MaxLength - link.Length;
        var text = body.ToString();

        if (text.Length > budget)
        {
            text = text[..(budget - 2)] + "…\n";
        }

        return text + link;
    }

    /// <summary>Neutraliza markdown e menções (@everyone, &lt;@id&gt;) vindos de fora.</summary>
    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length + 8);

        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '~' or '`' or '|' or '>' or '<')
            {
                builder.Append('\\');
            }

            builder.Append(c);

            if (c == '@')
            {
                builder.Append('​');
            }
        }

        return builder.ToString();
    }

    private static void AppendList(StringBuilder body, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        body.Append('\n').Append(title).Append(':');

        foreach (var item in items.Take(MaxItems))
        {
            body.Append("\n- ").Append(Escape(Shorten(item, MaxItemLength)));
        }

        body.Append('\n');
    }

    private static string Describe(WorkMode mode) => mode switch
    {
        WorkMode.Remote => "Remoto",
        WorkMode.Hybrid => "Híbrido",
        WorkMode.OnSite => "Presencial",
        _ => "não informada",
    };

    private static string Shorten(string text, int maxLength) => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
