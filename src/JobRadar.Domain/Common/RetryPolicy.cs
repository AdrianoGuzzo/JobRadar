namespace JobRadar.Domain.Common;

public enum FailureKind
{
    /// <summary>Pode dar certo numa nova tentativa (timeout, rate limit, indisponibilidade).</summary>
    Transient,

    /// <summary>Não adianta tentar de novo sem intervenção (credencial inválida, configuração).</summary>
    Permanent,
}

public readonly record struct RetryDecision(bool ShouldRetry, DateTimeOffset? NextAttemptAt)
{
    public static RetryDecision GiveUp { get; } = new(false, null);

    public static RetryDecision At(DateTimeOffset when) => new(true, when);
}

/// <summary>Novas tentativas controladas: backoff exponencial com teto e limite de tentativas.</summary>
public sealed class RetryPolicy
{
    public RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (maxAttempts < 1)
        {
            throw new DomainException("A política de novas tentativas precisa de pelo menos uma tentativa.");
        }

        if (baseDelay <= TimeSpan.Zero || maxDelay < baseDelay)
        {
            throw new DomainException("Os intervalos da política de novas tentativas são inválidos.");
        }

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay;
        MaxDelay = maxDelay;
    }

    public static RetryPolicy Default { get; } = new(maxAttempts: 5, baseDelay: TimeSpan.FromMinutes(1), maxDelay: TimeSpan.FromHours(1));

    public int MaxAttempts { get; }

    public TimeSpan BaseDelay { get; }

    public TimeSpan MaxDelay { get; }

    /// <param name="attemptsMade">Quantas tentativas já falharam, contando a atual.</param>
    public RetryDecision Decide(int attemptsMade, FailureKind kind, DateTimeOffset now)
    {
        if (kind == FailureKind.Permanent || attemptsMade >= MaxAttempts)
        {
            return RetryDecision.GiveUp;
        }

        var factor = Math.Pow(2, Math.Max(0, attemptsMade - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(BaseDelay.Ticks * factor, MaxDelay.Ticks));

        return RetryDecision.At(now + delay);
    }
}
