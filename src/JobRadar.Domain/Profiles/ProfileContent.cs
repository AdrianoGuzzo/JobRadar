using JobRadar.Domain.Jobs;

namespace JobRadar.Domain.Profiles;

/// <summary>
/// O que o candidato informa sobre si e sobre a vaga que procura. Vem de um
/// arquivo de configuração editável (config/profile.json), nunca do código.
/// </summary>
public sealed record ProfileContent(
    string Name,
    string? Headline,
    int? YearsOfExperience,
    IReadOnlyList<string> DesiredRoles,
    string? Seniority,
    IReadOnlyList<string> RequiredTechnologies,
    IReadOnlyList<string> DesiredTechnologies,
    IReadOnlyList<WorkMode> WorkModes,
    IReadOnlyList<string> Locations,
    string? SalaryExpectation,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Experience,
    IReadOnlyList<string> AdditionalPreferences);
