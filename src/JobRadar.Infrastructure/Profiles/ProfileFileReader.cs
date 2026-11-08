using System.Text.Json;
using System.Text.Json.Serialization;
using JobRadar.Domain.Jobs;
using JobRadar.Domain.Profiles;

namespace JobRadar.Infrastructure.Profiles;

/// <summary>Lê o perfil do candidato de <c>config/profile.json</c> (editável sem mexer no código).</summary>
public static class ProfileFileReader
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter<WorkMode>(allowIntegerValues: false) },
    };

    public static ProfileContent Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Arquivo de perfil não encontrado em \"{path}\". Copie config/profile.example.json para esse caminho e ajuste (Profile__Path).");
        }

        ProfileFile? file;

        try
        {
            file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"O perfil em \"{path}\" é inválido: {exception.Message}", exception);
        }

        if (string.IsNullOrWhiteSpace(file?.Name))
        {
            throw new InvalidOperationException($"O perfil em \"{path}\" precisa do campo \"name\".");
        }

        return new ProfileContent(
            Name: file.Name.Trim(),
            Headline: file.Headline,
            YearsOfExperience: file.YearsOfExperience,
            DesiredRoles: file.DesiredRoles ?? [],
            Seniority: file.Seniority,
            RequiredTechnologies: file.RequiredTechnologies ?? [],
            DesiredTechnologies: file.DesiredTechnologies ?? [],
            WorkModes: file.WorkModes ?? [],
            Locations: file.Locations ?? [],
            SalaryExpectation: file.SalaryExpectation,
            Languages: file.Languages ?? [],
            Experience: file.Experience ?? [],
            AdditionalPreferences: file.AdditionalPreferences ?? []);
    }

    private sealed record ProfileFile(
        string? Name,
        string? Headline,
        int? YearsOfExperience,
        List<string>? DesiredRoles,
        string? Seniority,
        List<string>? RequiredTechnologies,
        List<string>? DesiredTechnologies,
        List<WorkMode>? WorkModes,
        List<string>? Locations,
        string? SalaryExpectation,
        List<string>? Languages,
        List<string>? Experience,
        List<string>? AdditionalPreferences);
}
