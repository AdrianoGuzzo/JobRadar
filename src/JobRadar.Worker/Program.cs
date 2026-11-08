using System.Globalization;
using Hangfire;
using Hangfire.PostgreSql;
using JobRadar.Worker.Composition;
using JobRadar.Worker.Jobs;
using JobRadar.Worker.Startup;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddJobRadarCore(builder.Configuration);

    var connectionString = builder.Configuration.GetConnectionString("JobRadar")!;

    // O Hangfire guarda a agenda no mesmo PostgreSQL, num schema próprio (ADR-004).
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            storage => storage.UseNpgsqlConnection(connectionString),
            new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));

    builder.Services.AddHangfireServer(server =>
    {
        server.WorkerCount = 3;
        server.ServerName = $"jobradar-{Environment.MachineName}";
    });

    using var host = builder.Build();

    host.Services.GetRequiredService<IStartupValidator>().Validate();
    await host.Services.GetRequiredService<StartupTasks>().RunAsync(CancellationToken.None);
    RecurringJobRegistrar.Register(
        host.Services.GetRequiredService<IRecurringJobManager>(),
        host.Services.GetRequiredService<IOptions<MonitoringOptions>>().Value);

    Log.Information("JobRadar iniciado.");
    await host.RunAsync();
    return 0;
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "O JobRadar parou: {Message}", exception.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
