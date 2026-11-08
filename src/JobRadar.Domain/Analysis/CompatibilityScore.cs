using JobRadar.Domain.Common;

namespace JobRadar.Domain.Analysis;

/// <summary>Compatibilidade entre a vaga e o perfil, de 0 a 100.</summary>
public readonly record struct CompatibilityScore
{
    public const int Min = 0;

    public const int Max = 100;

    public CompatibilityScore(int value)
    {
        if (value is < Min or > Max)
        {
            throw new DomainException($"O score de compatibilidade deve estar entre {Min} e {Max}; recebido {value}.");
        }

        Value = value;
    }

    public int Value { get; }
}
