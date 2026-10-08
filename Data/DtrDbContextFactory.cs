using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace DepEdDTRSystem.Data;

public class DtrDbContextFactory : IDesignTimeDbContextFactory<DtrDbContext>
{
    public DtrDbContext CreateDbContext(string[] args)
    {
        var environment =
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var basePath = Directory.GetCurrentDirectory();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile(
                "appsettings.json",
                optional: true,
                reloadOnChange: false)
            .AddJsonFile(
                $"appsettings.{environment}.json",
                optional: true,
                reloadOnChange: false)
            .AddUserSecrets(
                Assembly.GetExecutingAssembly(),
                optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "The database connection string 'DefaultConnection' could not be found.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<DtrDbContext>();

        optionsBuilder.UseNpgsql(connectionString);

        return new DtrDbContext(optionsBuilder.Options);
    }
}