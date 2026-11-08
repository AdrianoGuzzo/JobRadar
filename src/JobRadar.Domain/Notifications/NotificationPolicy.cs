using JobRadar.Domain.Analysis;
using JobRadar.Domain.Common;

namespace JobRadar.Domain.Notifications;

/// <summary>Quando uma análise vira notificação: score mínimo e nunca duas vezes.</summary>
public sealed class NotificationPolicy
{
    public const int DefaultMinimumScore = 75;

    public NotificationPolicy(int minimumScore = DefaultMinimumScore)
    {
        if (minimumScore is < CompatibilityScore.Min or > CompatibilityScore.Max)
        {
            throw new DomainException($"O score mínimo para notificar deve estar entre {CompatibilityScore.Min} e {CompatibilityScore.Max}; recebido {minimumScore}.");
        }

        MinimumScore = minimumScore;
    }

    public int MinimumScore { get; }

    public bool ShouldNotify(JobMatch match, bool alreadyNotified) => !alreadyNotified && match.Score >= MinimumScore;
}
