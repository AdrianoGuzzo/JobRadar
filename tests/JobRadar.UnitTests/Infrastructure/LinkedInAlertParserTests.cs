using JobRadar.Domain.Jobs;
using JobRadar.Infrastructure.LinkedIn;
using MimeKit;

namespace JobRadar.UnitTests.Infrastructure;

/// <summary>
/// As fixtures em Fixtures/LinkedIn são sintéticas, inspiradas na estrutura dos
/// alertas reais. Acrescente e-mails reais anonimizados para cobrir mudanças de layout.
/// </summary>
public sealed class LinkedInAlertParserTests
{
    [Fact]
    public void Should_Extract_All_Jobs_From_Html_Alert()
    {
        var postings = LinkedInAlertParser.Parse(Load("alerta-html-tres-vagas.eml"));

        postings.Select(p => p.ExternalId).Should().Equal("4012345678", "4098765432", "4055555555");
    }

    [Fact]
    public void Should_Read_Title_Company_Location_And_Work_Mode()
    {
        var postings = LinkedInAlertParser.Parse(Load("alerta-html-tres-vagas.eml"));

        var first = postings[0];
        first.Title.Should().Be("Desenvolvedor .NET Sênior");
        first.Company.Should().Be("Agro Tech");
        first.Location.Should().Be("São Paulo, SP");
        first.WorkMode.Should().Be(WorkMode.Remote);
        first.Description.Should().BeNull("o alerta não traz a descrição da vaga");

        var second = postings[1];
        second.Title.Should().Be("Engenheiro de Software Backend (C#)");
        second.Company.Should().Be("Empresa Exemplo");
        second.Location.Should().Be("Curitiba, PR");
        second.WorkMode.Should().Be(WorkMode.Hybrid);
    }

    [Fact]
    public void Should_Keep_Job_With_Partial_Information()
    {
        var postings = LinkedInAlertParser.Parse(Load("alerta-html-tres-vagas.eml"));

        var partial = postings[2];
        partial.Title.Should().Be("Tech Lead .NET");
        partial.Company.Should().Be("Fintech X");
        partial.Location.Should().BeNull();
        partial.WorkMode.Should().Be(WorkMode.NotInformed);
    }

    [Fact]
    public void Should_Use_Canonical_Url_Without_Tracking()
    {
        var postings = LinkedInAlertParser.Parse(Load("alerta-html-tres-vagas.eml"));

        postings.Select(p => p.Url).Should().Equal(
            "https://www.linkedin.com/jobs/view/4012345678/",
            "https://www.linkedin.com/jobs/view/4098765432/",
            "https://www.linkedin.com/jobs/view/4055555555/");
    }

    [Fact]
    public void Should_Return_No_Jobs_When_Alert_Has_None()
    {
        LinkedInAlertParser.Parse(Load("alerta-sem-vagas.eml")).Should().BeEmpty();
    }

    [Fact]
    public void Should_Fall_Back_To_Plain_Text_Alert()
    {
        var posting = LinkedInAlertParser.Parse(Load("alerta-somente-texto.eml")).Should().ContainSingle().Subject;

        posting.ExternalId.Should().Be("4077777777");
        posting.Title.Should().Be("Desenvolvedor Full Stack (React + .NET)");
        posting.Company.Should().Be("Cooperativa Agro");
        posting.Location.Should().Be("Goiânia, GO");
        posting.WorkMode.Should().Be(WorkMode.OnSite);
    }

    [Fact]
    public void Should_Recognize_Only_LinkedIn_Senders()
    {
        LinkedInAlertParser.IsLinkedInAlert(Load("alerta-html-tres-vagas.eml")).Should().BeTrue();
        LinkedInAlertParser.IsLinkedInAlert(Load("nao-linkedin.eml")).Should().BeFalse();
    }

    [Fact]
    public void Should_Ignore_Unknown_Layout_Without_Failing()
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("LinkedIn", "jobalerts-noreply@linkedin.com"));
        message.Body = new TextPart("html") { Text = "<html><body><div><a href='https://www.linkedin.com/jobs/view/123456789/'><img src='x.png'></a></div></body></html>" };

        LinkedInAlertParser.Parse(message).Should().BeEmpty("sem título a vaga não pode ser registrada");
    }

    internal static MimeMessage Load(string name) =>
        MimeMessage.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "LinkedIn", name));
}
