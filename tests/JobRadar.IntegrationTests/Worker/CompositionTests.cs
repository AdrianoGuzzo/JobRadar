using Hangfire;
using JobRadar.Application.Abstractions;
using JobRadar.Infrastructure.Claude;
using JobRadar.Infrastructure.Codex;
using JobRadar.Domain.Notifications;
using JobRadar.Worker.Jobs;
using JobRadar.Worker.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JobRadar.IntegrationTests.Worker;

[Trait("Category", "Integration")]
public sealed class CompositionTests
{
    [Theory]
    [InlineData("Claude", typeof(ClaudeCodeJobAnalyzer))]
    [InlineData("Codex", typeof(CodexJobAnalyzer))]
    [InlineData("codex", typeof(CodexJobAnalyzer))]
    public void Should_Select_Configured_Cli(string name, Type analyzerType)
    {
        var settings = WorkerTestHost.Settings();
        settings["Analysis:Provider"] = name;
        using var provider = WorkerTestHost.Build(settings);

        provider.GetRequiredService<IJobAnalyzer>().Should().BeOfType(analyzerType);
    }

    [Theory]
    [InlineData("Claude", "Codex")]
    [InlineData("Codex", "Claude")]
    public void Should_Validate_Only_Selected_Provider(string selected, string unused)
    {
        var settings = WorkerTestHost.Settings();
        settings["Analysis:Provider"] = selected;
        settings[unused + ":MaxConcurrency"] = "0";
        using var provider = WorkerTestHost.Build(settings);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void Should_Reject_Unknown_Provider()
    {
        var settings = WorkerTestHost.Settings();
        settings["Analysis:Provider"] = "unknown";

        var act = () => WorkerTestHost.Build(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Analysis*Provider*");
    }

    [Fact]
    public void Should_Resolve_Recurring_Jobs_And_Startup_Tasks()
    {
        using var provider = WorkerTestHost.Build(WorkerTestHost.Settings());

        provider.GetRequiredService<RecurringJobs>().Should().NotBeNull();
        provider.GetRequiredService<StartupTasks>().Should().NotBeNull();
        provider.GetServices<IJobSource>().Should().ContainSingle();
    }

    [Fact]
    public void Should_Use_75_As_Default_Minimum_Score()
    {
        using var provider = WorkerTestHost.Build(WorkerTestHost.Settings());

        provider.GetRequiredService<NotificationPolicy>().MinimumScore.Should().Be(75);
    }

    [Fact]
    public void Should_Use_Configured_Minimum_Score()
    {
        var settings = WorkerTestHost.Settings();
        settings["Notification:MinimumScore"] = "80";
        using var provider = WorkerTestHost.Build(settings);

        provider.GetRequiredService<NotificationPolicy>().MinimumScore.Should().Be(80);
    }

    [Fact]
    public void Should_Fail_Fast_When_A_Required_Secret_Is_Missing()
    {
        var settings = WorkerTestHost.Settings();
        settings.Remove("Discord:BotToken");
        using var provider = WorkerTestHost.Build(settings);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*BotToken*");
    }

    [Fact]
    public void Should_Fail_Fast_When_Minimum_Score_Is_Out_Of_Range()
    {
        var settings = WorkerTestHost.Settings();
        settings["Notification:MinimumScore"] = "150";
        using var provider = WorkerTestHost.Build(settings);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*MinimumScore*");
    }

    [Fact]
    public void Should_Fail_Fast_Without_Connection_String()
    {
        var settings = WorkerTestHost.Settings();
        settings.Remove("ConnectionStrings:JobRadar");

        var act = () => WorkerTestHost.Build(settings);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__JobRadar*");
    }

    [Theory]
    [InlineData(nameof(RecurringJobs.CollectAsync))]
    [InlineData(nameof(RecurringJobs.AnalyzeAsync))]
    [InlineData(nameof(RecurringJobs.NotifyAsync))]
    public void Recurring_Jobs_Should_Neither_Retry_Nor_Overlap_In_Hangfire(string method)
    {
        var info = typeof(RecurringJobs).GetMethod(method)!;

        info.GetCustomAttributes(typeof(AutomaticRetryAttribute), true).Cast<AutomaticRetryAttribute>()
            .Should().ContainSingle().Which.Attempts.Should().Be(0, "o retry é do JobRadar, por vaga e por notificação (ADR-004)");
        info.GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), true).Should().ContainSingle();
    }
}
