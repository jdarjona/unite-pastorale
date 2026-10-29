using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UnidadPastoral.Api.Data;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "PostgreSql";
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=unidad_pastoral;Username=postgres;Password=postgres";
        connectionString = PostgresConnectionString.Normalize(connectionString);
        var options = new DbContextOptionsBuilder<AppDbContext>();

        if (string.Equals(provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
        {
            options.UseNpgsql(connectionString);
        }
        else if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(connectionString);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use Sqlite or PostgreSql.");
        }

        return new AppDbContext(options.Options);
    }
}