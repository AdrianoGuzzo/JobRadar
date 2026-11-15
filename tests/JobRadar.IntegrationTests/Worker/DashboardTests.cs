using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Storage;
using JobRadar.Worker.Dashboard;
using JobRadar.Worker.Jobs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobRadar.IntegrationTests.Worker;

[Trait("Category", "Integration")]
public sealed class DashboardTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Should_Protect_Dashboard_And_Enqueue_Recurring_Job_Without_Changing_Schedule()
    {
        var ct = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateEmptyDatabaseAsync();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAntiforgery();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dashboard:Password"] = "dashboard-test-password",
        });
        builder.Services.AddHangfire(configuration => configuration.UsePostgreSqlStorage(
            options => options.UseNpgsqlConnection(connectionString)));
        await using var app = builder.Build();
        app.UseHangfireDashboard("/rotinas", new DashboardOptions
        {
            Authorization = [new DashboardAuthorizationFilter(builder.Configuration)],
        });
        var manager = app.Services.GetRequiredService<IRecurringJobManager>();
        RecurringJobRegistrar.Register(manager, new MonitoringOptions());
        await app.StartAsync(ct);

        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var anonymous = await client.GetAsync("/rotinas", ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("admin:dashboard-test-password")));
        using var dashboard = await client.GetAsync("/rotinas/recurring", ct);
        dashboard.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await dashboard.Content.ReadAsStringAsync(ct);
        html.Should().Contain(RecurringJobRegistrar.CollectId).And.Contain(RecurringJobRegistrar.AnalyzeId)
            .And.Contain(RecurringJobRegistrar.NotifyId);

        // O endpoint de disparo mantém a proteção CSRF nativa do Hangfire.
        using var unsafeTrigger = await client.PostAsync("/rotinas/recurring/trigger",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["jobs[]"] = RecurringJobRegistrar.CollectId }), ct);
        unsafeTrigger.IsSuccessStatusCode.Should().BeFalse();

        var token = Regex.Match(html, "<meta name=\"csrf-token\" content=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups[1].Value;
        token.Should().NotBeNullOrEmpty();
        client.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(token));
        using var trigger = await client.PostAsync("/rotinas/recurring/trigger",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["jobs[]"] = RecurringJobRegistrar.CollectId }), ct);
        trigger.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var connection = app.Services.GetRequiredService<JobStorage>().GetConnection();
        var routine = connection.GetRecurringJobs().Single(job => job.Id == RecurringJobRegistrar.CollectId);
        routine.Cron.Should().Be(new MonitoringOptions().CollectCron);
        routine.LastJobId.Should().NotBeNullOrEmpty();
        connection.GetStateData(routine.LastJobId).Name.Should().Be("Enqueued");

        await app.StopAsync(ct);
    }
}
