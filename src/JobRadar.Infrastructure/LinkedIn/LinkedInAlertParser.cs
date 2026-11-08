using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;
using MimeKit;

namespace JobRadar.Infrastructure.LinkedIn;

/// <summary>
/// Extrai as vagas de um e-mail de alerta do LinkedIn, sem acessar o site.
/// Usa só o que o e-mail traz: links <c>/jobs/view/{id}</c> e o texto ao redor.
/// O alerta não traz a descrição da vaga, então ela fica em branco e aparece
/// como informação ausente na análise.
/// </summary>
public static partial class LinkedInAlertParser
{
    private static readonly string[] CallsToAction = ["ver vaga", "view job", "see job", "candidatar-se", "candidate-se", "apply", "aplicar", "ver todas"];

    private static readonly string[] Noise =
    [
        "candidatura simplificada", "easy apply", "promovida", "promoted", "recrutando ativamente", "actively recruiting",
        "novo", "new", "ver vaga", "view job",
    ];

    public static bool IsLinkedInAlert(MimeMessage message) =>
        message.From.Mailboxes.Any(mailbox =>
            mailbox.Domain.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase)
            || mailbox.Domain.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<JobPosting> Parse(MimeMessage message)
    {
        if (message.HtmlBody is { Length: > 0 } html)
        {
            return ParseHtml(html);
        }

        return message.TextBody is { Length: > 0 } text ? ParseText(text) : [];
    }

    public static string CanonicalUrl(string externalId) => $"https://www.linkedin.com/jobs/view/{externalId}/";

    /// <summary>O id numérico de um link de vaga do LinkedIn, ou <c>null</c>.</summary>
    public static string? ExtractJobId(string? url)
    {
        if (url is null || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) && !uri.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var match = JobViewPath().Match(uri.AbsolutePath);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static List<JobPosting> ParseHtml(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var anchorsByJob = new Dictionary<string, List<IElement>>();
        var order = new List<string>();

        foreach (var anchor in document.QuerySelectorAll("a[href]"))
        {
            if (ExtractJobId(anchor.GetAttribute("href")) is not { } id)
            {
                continue;
            }

            if (!anchorsByJob.TryGetValue(id, out var anchors))
            {
                anchorsByJob[id] = anchors = [];
                order.Add(id);
            }

            anchors.Add(anchor);
        }

        var postings = new List<JobPosting>();

        foreach (var id in order)
        {
            var titleAnchor = anchorsByJob[id].FirstOrDefault(anchor => IsTitle(Clean(anchor.TextContent)));

            if (titleAnchor is null)
            {
                continue;
            }

            var details = DetailLines(titleAnchor);
            postings.Add(Build(id, Clean(titleAnchor.TextContent), details));
        }

        return postings;
    }

    /// <summary>Os textos que seguem o título no mesmo cartão (empresa, local, selos).</summary>
    private static List<string> DetailLines(IElement titleAnchor)
    {
        var start = titleAnchor;

        while (start.NextElementSibling is null && start.ParentElement is { LocalName: not ("td" or "tr" or "table" or "body") } parent)
        {
            start = parent;
        }

        var lines = new List<string>();

        for (var sibling = start.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (sibling.LocalName == "a" || sibling.QuerySelector("a[href*='/jobs/view/']") is not null)
            {
                continue;
            }

            var text = Clean(sibling.TextContent);

            if (text.Length > 0)
            {
                lines.Add(text);
            }
        }

        return lines;
    }

    private static List<JobPosting> ParseText(string text)
    {
        var lines = text.Split('\n').Select(line => Clean(line)).ToList();
        var postings = new List<JobPosting>();
        var seen = new HashSet<string>();

        for (var i = 0; i < lines.Count; i++)
        {
            var url = UrlInText().Match(lines[i]);

            if (!url.Success || ExtractJobId(url.Value) is not { } id || !seen.Add(id))
            {
                continue;
            }

            var block = new List<string>();

            for (var j = i - 1; j >= 0 && lines[j].Length > 0; j--)
            {
                block.Insert(0, lines[j]);
            }

            if (block.Count > 0 && IsTitle(block[0]))
            {
                postings.Add(Build(id, block[0], block.Skip(1).ToList()));
            }
        }

        return postings;
    }

    private static JobPosting Build(string id, string title, IReadOnlyList<string> details)
    {
        var parts = details
            .SelectMany(line => line.Split(['·', '•', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(part => !Noise.Contains(part, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var company = parts.Count > 0 ? parts[0] : null;
        var (location, workMode) = parts.Count > 1 ? ParseLocation(parts[1]) : (null, WorkMode.NotInformed);

        return new JobPosting(
            Title: title,
            Url: CanonicalUrl(id),
            ExternalId: id,
            Company: company,
            Location: location,
            WorkMode: workMode,
            Description: null);
    }

    /// <summary>"São Paulo, SP (Remoto)" vira ("São Paulo, SP", Remote).</summary>
    internal static (string? Location, WorkMode Mode) ParseLocation(string text)
    {
        var mode = WorkMode.NotInformed;
        var location = text;
        var match = TrailingParenthesis().Match(text);

        if (match.Success && ToWorkMode(match.Groups["mode"].Value) is { } inParenthesis)
        {
            mode = inParenthesis;
            location = text[..match.Index].Trim();
        }
        else if (ToWorkMode(text) is { } whole)
        {
            mode = whole;
            location = string.Empty;
        }

        return (location.Length > 0 ? location : null, mode);
    }

    private static WorkMode? ToWorkMode(string text) => text.Trim().ToLowerInvariant() switch
    {
        "remoto" or "remote" or "home office" => WorkMode.Remote,
        "híbrido" or "hibrido" or "hybrid" => WorkMode.Hybrid,
        "presencial" or "on-site" or "onsite" or "no local" => WorkMode.OnSite,
        _ => null,
    };

    private static bool IsTitle(string text) =>
        text.Length is > 1 and <= 300
        && !CallsToAction.Any(cta => text.StartsWith(cta, StringComparison.OrdinalIgnoreCase));

    private static string Clean(string text) => Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"/jobs/view/(?:[^/?#]*?-)?(?<id>\d{5,})/?$", RegexOptions.IgnoreCase)]
    private static partial Regex JobViewPath();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlInText();

    [GeneratedRegex(@"\((?<mode>[^()]+)\)\s*$")]
    private static partial Regex TrailingParenthesis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
