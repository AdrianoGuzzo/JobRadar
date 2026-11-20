using Discord;
using JobRadar.Domain.Jobs;

namespace JobRadar.Infrastructure.Discord;

public static class JobReviewComponents
{
    public const string Prefix = "job-review:v1:";

    public static MessageComponent Build(Guid notificationId, JobReviewStatus status) => new ComponentBuilder()
        .WithButton("Gostei", $"{Prefix}approve:{notificationId:N}",
            status == JobReviewStatus.Approved ? ButtonStyle.Success : ButtonStyle.Secondary, new Emoji("👍"))
        .WithButton("Descartar", $"{Prefix}discard:{notificationId:N}",
            status == JobReviewStatus.Discarded ? ButtonStyle.Danger : ButtonStyle.Secondary, new Emoji("👎"))
        .WithButton(Label(status), "job-review:status", ButtonStyle.Secondary, disabled: true)
        .Build();

    public static string Label(JobReviewStatus status) => status switch
    {
        JobReviewStatus.Approved => "Aprovada",
        JobReviewStatus.Discarded => "Descartada",
        _ => "Pendente",
    };

    public static bool TryParse(string value, out Guid notificationId, out JobReviewStatus status)
    {
        notificationId = default;
        status = JobReviewStatus.Pending;
        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = value[Prefix.Length..].Split(':');
        if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "N", out notificationId))
        {
            return false;
        }

        status = parts[0] switch
        {
            "approve" => JobReviewStatus.Approved,
            "discard" => JobReviewStatus.Discarded,
            _ => JobReviewStatus.Pending,
        };
        return status != JobReviewStatus.Pending && notificationId != Guid.Empty;
    }
}
