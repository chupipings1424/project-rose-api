using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Rose.Infrastructure.Lingo;

/// <summary>
/// Used only by the <c>dotnet ef</c> tool (migrations add / database update) so it can build
/// a LingoDbContext without starting the whole API. The running API never calls this.
/// </summary>
internal sealed class LingoDesignTimeFactory : IDesignTimeDbContextFactory<LingoDbContext>
{
    public LingoDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<LingoDbContext>().UseSqlite("Data Source=lingo-design.db").Options);
}
