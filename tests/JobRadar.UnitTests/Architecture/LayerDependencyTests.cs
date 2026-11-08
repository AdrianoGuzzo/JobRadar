using System.Reflection;

namespace JobRadar.UnitTests.Architecture;

/// <summary>
/// As camadas internas não podem depender de tecnologia de infraestrutura.
/// O MSBuild já impede ciclos entre projetos; aqui se garante que Domain e
/// Application não passem a usar banco, Discord, e-mail ou agendador.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly string[] InfrastructureTechnologies =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "Discord",
        "MailKit",
        "MimeKit",
        "AngleSharp",
        "Hangfire",
        "Serilog",
        "Microsoft.Extensions.Hosting",
    ];

    [Fact]
    public void Domain_Should_Not_Reference_Other_JobRadar_Assemblies()
    {
        var references = ReferencedAssemblyNames("JobRadar.Domain");

        references.Should().NotContain(name => name.StartsWith("JobRadar.", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_Should_Not_Reference_Infrastructure_Technologies()
    {
        var references = ReferencedAssemblyNames("JobRadar.Domain");

        references.Should().NotContain(name => IsInfrastructureTechnology(name));
    }

    [Fact]
    public void Application_Should_Not_Reference_Infrastructure()
    {
        var references = ReferencedAssemblyNames("JobRadar.Application");

        references.Should().NotContain(name =>
            name == "JobRadar.Infrastructure" || name == "JobRadar.Worker" || IsInfrastructureTechnology(name));
    }

    private static bool IsInfrastructureTechnology(string assemblyName) =>
        InfrastructureTechnologies.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal));

    private static List<string> ReferencedAssemblyNames(string assemblyName) =>
        [.. Assembly.Load(new AssemblyName(assemblyName))
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)];
}
