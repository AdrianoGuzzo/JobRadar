using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobRadar.Infrastructure.Persistence;

/// <summary>
/// Só para o <c>dotnet ef</c> gerar migrations: não abre conexão. Para aplicar
/// com a ferramenta, defina <c>ConnectionStrings__JobRadar</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<JobRadarDbContext>
{
    public JobRadarDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__JobRadar")
            ?? "Host=localhost;Port=5434;Database=jobradar;Username=jobradar";

        return new JobRadarDbContext(new DbContextOptionsBuilder<JobRadarDbContext>().UseJobRadarPostgres(connectionString).Options);
    }
}
