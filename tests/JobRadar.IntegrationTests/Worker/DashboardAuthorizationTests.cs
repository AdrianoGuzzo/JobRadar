using System.Net;
using System.Text;
using JobRadar.Worker.Dashboard;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace JobRadar.IntegrationTests.Worker;

public sealed class DashboardAuthorizationTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("192.168.100.10", false)]
    public void Should_Restrict_Access_To_Loopback_Without_Password(string address, bool allowed)
    {
        var filter = new DashboardAuthorizationFilter(new ConfigurationBuilder().Build());
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        filter.Authorize(context).Should().Be(allowed);
    }

    [Theory]
    [InlineData("admin:senha-teste", true)]
    [InlineData("admin:errada", false)]
    [InlineData("outro:senha-teste", false)]
    [InlineData("", false)]
    [InlineData("malformed", false)]
    public void Should_Require_Valid_Credentials_When_Password_Is_Configured(string credentials, bool allowed)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dashboard:Password"] = "senha-teste",
        }).Build();
        var filter = new DashboardAuthorizationFilter(configuration);
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.Authorization = credentials == "malformed"
            ? "Basic %%%" : "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));

        filter.Authorize(context).Should().Be(allowed);
        if (!allowed)
        {
            context.Response.StatusCode.Should().Be(401);
            context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Basic");
        }
    }
}
