using JobRadar.Domain.Common;

namespace JobRadar.Domain.Jobs;

/// <summary>
/// Normaliza o link da vaga: só http(s), sem fragmento e sem os parâmetros de
/// rastreamento que mudam a cada e-mail (ADR-001).
/// </summary>
public static class JobUrl
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "trk", "trkInfo", "trackingId", "refId", "lipi", "midToken", "midSig", "eBP", "otpToken", "recommendedFlavor", "ref", "src",
    };

    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new DomainException($"Link de vaga inválido: \"{url}\".");
        }

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair => !IsTracking(pair.Split('=', 2)[0]));

        var query = string.Join('&', kept);
        var builder = new UriBuilder(uri) { Query = query, Fragment = string.Empty };

        return builder.Uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    private static bool IsTracking(string name) =>
        TrackingParameters.Contains(name) || name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
}
