using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobRadar.Domain.Common;

namespace JobRadar.Domain.Profiles;

/// <summary>
/// O perfil profissional usado nas análises. A <see cref="Version"/> é o hash
/// do conteúdo: uma vaga é analisada uma vez por versão do perfil (ADR-002).
/// </summary>
public sealed class CandidateProfile
{
    private CandidateProfile()
    {
        Content = null!;
        Version = string.Empty;
    }

    public Guid Id { get; private set; }

    public ProfileContent Content { get; private set; }

    public string Version { get; private set; }

    public static CandidateProfile Create(ProfileContent content)
    {
        Validate(content);
        return new CandidateProfile { Id = Guid.CreateVersion7(), Content = content, Version = ComputeVersion(content) };
    }

    /// <summary>Troca o conteúdo; devolve <c>true</c> se ele mudou de fato.</summary>
    public bool Update(ProfileContent content)
    {
        Validate(content);
        var version = ComputeVersion(content);

        if (version == Version)
        {
            return false;
        }

        Content = content;
        Version = version;
        return true;
    }

    private static void Validate(ProfileContent content)
    {
        if (string.IsNullOrWhiteSpace(content.Name))
        {
            throw new DomainException("O perfil precisa de um nome.");
        }
    }

    private static string ComputeVersion(ProfileContent content)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(content);
        return Convert.ToHexStringLower(SHA256.HashData(json))[..16];
    }
}
