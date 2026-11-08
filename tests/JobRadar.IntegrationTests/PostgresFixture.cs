using JobRadar.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(JobRadar.IntegrationTests.PostgresFixture))]

namespace JobRadar.IntegrationTests;

/// <summary>
/// Um PostgreSQL de verdade (Testcontainers) para a suíte inteira. Cada teste
/// pede um banco novo e já migrado, então os testes não se enxergam.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Cria um banco vazio e devolve a connection string dele.</summary>
    public async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"jobradar_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    /// <summary>Cria um banco novo e aplica as migrations.</summary>
    public async Task<TestDatabase> CreateMigratedDatabaseAsync()
    {
        var database = new TestDatabase(await CreateEmptyDatabaseAsync());

        await using var context = database.CreateContext();
        await context.Database.MigrateAsync();

        return database;
    }
}

public sealed class TestDatabase(string connectionString) : IDbContextFactory<JobRadarDbContext>
{
    public string ConnectionString { get; } = connectionString;

    public JobRadarDbContext CreateContext() => CreateDbContext();

    public JobRadarDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<JobRadarDbContext>().UseJobRadarPostgres(ConnectionString).Options);
}
