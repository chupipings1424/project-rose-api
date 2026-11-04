using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without starting the API. Creating a
/// migration never connects, so the local docker-compose database is only a default.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RoseDbContext>
{
    public RoseDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RoseDbContext>();
        RoseDbContext.Configure(
            options,
            Environment.GetEnvironmentVariable("ConnectionStrings__Rose")
                ?? "Host=localhost;Port=5433;Database=rose;Username=rose;Password=rose");
        return new RoseDbContext(options.Options);
    }
}