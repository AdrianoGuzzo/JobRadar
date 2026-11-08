using JobRadar.Infrastructure.Security;

namespace JobRadar.UnitTests.Infrastructure;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Should_Redact_Anthropic_Keys_And_Tokens()
    {
        var text = SecretRedactor.Redact("falhou com sk-ant-oat01-AbCdEf0123456789_xyz no header");

        text.Should().NotContain("sk-ant-oat01").And.Contain(SecretRedactor.Mask);
    }

    [Fact]
    public void Should_Redact_Discord_Bot_Token()
    {
        const string token = "MTA5ODc2NTQzMjEwOTg3NjU0Mw" + ".GaBcDe.abcdefghijklmnopqrstuvwxyz0123456789AB";

        SecretRedactor.Redact($"token={token}").Should().NotContain(token);
    }

    [Fact]
    public void Should_Redact_Authorization_Header_Values()
    {
        var text = SecretRedactor.Redact("Authorization: Bot abcdefghijklmnop1234567890 e Bearer zyxwvutsrqponmlk0987654321");

        text.Should().Be($"Authorization: Bot {SecretRedactor.Mask} e Bearer {SecretRedactor.Mask}");
    }

    [Fact]
    public void Should_Redact_Known_Secret_Values()
    {
        var text = SecretRedactor.Redact("senha do imap: minha-senha-de-app", "minha-senha-de-app");

        text.Should().Be($"senha do imap: {SecretRedactor.Mask}");
    }

    [Fact]
    public void Should_Keep_Ordinary_Text()
    {
        SecretRedactor.Redact("O Claude excedeu o tempo limite.").Should().Be("O Claude excedeu o tempo limite.");
    }
}
