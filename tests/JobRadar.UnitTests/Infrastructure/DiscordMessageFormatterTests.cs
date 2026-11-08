using JobRadar.Application.Abstractions;
using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.Discord;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class DiscordMessageFormatterTests
{
    [Fact]
    public void Should_Format_Message_Like_The_Specified_Example()
    {
        var text = DiscordMessageFormatter.Format(Message());

        text.Should().StartWith("**NOVA OPORTUNIDADE!**");
        text.Should().Contain("**Senior .NET Developer**");
        text.Should().Contain("Compatibilidade: **92%**");
        text.Should().Contain("Modalidade: Remoto");
        text.Should().Contain("Localização: Brasil");
        text.Should().Contain("Origem: LinkedIn");
        text.Should().Contain("Pontos fortes:\n- C# / .NET\n- APIs REST");
        text.Should().Contain("Pontos de atenção:\n- Inglês avançado");
        text.Should().EndWith("Link: https://www.linkedin.com/jobs/view/123/");
    }

    [Fact]
    public void Should_Show_Not_Informed_For_Missing_Fields()
    {
        var text = DiscordMessageFormatter.Format(Message() with { WorkMode = WorkMode.NotInformed, Location = null, Company = null });

        text.Should().Contain("Modalidade: não informada");
        text.Should().Contain("Localização: não informada");
    }

    [Fact]
    public void Should_Explicit_Insufficient_Information()
    {
        var text = DiscordMessageFormatter.Format(Message() with { MissingInformation = ["descrição", "faixa salarial"] });

        text.Should().Contain("Informações não disponíveis: descrição, faixa salarial");
    }

    [Fact]
    public void Should_Omit_Insufficient_Information_Section_When_Complete()
    {
        DiscordMessageFormatter.Format(Message()).Should().NotContain("Informações não disponíveis");
    }

    [Fact]
    public void Should_Escape_Markdown_And_Mentions_From_External_Data()
    {
        var text = DiscordMessageFormatter.Format(Message() with { Title = "**Vaga** @everyone _urgente_ <@123>", Strengths = ["`código`"] });

        text.Should().NotContain("@everyone");
        text.Should().NotContain("<@123>");
        text.Should().Contain("\\*\\*Vaga\\*\\*");
        text.Should().Contain("\\_urgente\\_");
        text.Should().Contain("\\`código\\`");
    }

    [Fact]
    public void Should_Never_Exceed_Discord_Limit_And_Keep_The_Link()
    {
        var huge = Enumerable.Range(0, 40).Select(i => new string('x', 250) + i).ToList();

        var text = DiscordMessageFormatter.Format(Message() with { Strengths = huge, Gaps = huge, Summary = new string('y', 3000) });

        text.Length.Should().BeLessThanOrEqualTo(DiscordMessageFormatter.MaxLength);
        text.Should().EndWith("Link: https://www.linkedin.com/jobs/view/123/");
    }

    private static JobNotificationMessage Message() => new(
        Title: "Senior .NET Developer",
        Company: "Agro Tech",
        Score: 92,
        WorkMode: WorkMode.Remote,
        Location: "Brasil",
        SourceName: "LinkedIn",
        Strengths: ["C# / .NET", "APIs REST"],
        Gaps: ["Inglês avançado"],
        MissingInformation: [],
        Summary: "Alta compatibilidade com o perfil profissional.",
        Url: "https://www.linkedin.com/jobs/view/123/");
}
