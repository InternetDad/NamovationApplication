using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Namovation.Infrastructure.Data;

namespace Namovation.Web;

public sealed class NamovationDesignTimeFactory : IDesignTimeDbContextFactory<NamovationDbContext>
{
    public NamovationDbContext CreateDbContext(string[] args)
    {
        var currentPath = Directory.GetCurrentDirectory();
        var webPath = new[] {
            Path.Combine(currentPath, "Namovation.Web"),
            Path.Combine(currentPath, "..", "Namovation.Web"),
            currentPath
        }.Select(Path.GetFullPath).FirstOrDefault(path => File.Exists(Path.Combine(path, "appsettings.json")))
          ?? throw new DirectoryNotFoundException("Could not locate Namovation.Web/appsettings.json from the current directory.");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(webPath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");
        var options = new DbContextOptionsBuilder<NamovationDbContext>().UseNpgsql(connectionString).Options;
        return new NamovationDbContext(options);
    }
}
