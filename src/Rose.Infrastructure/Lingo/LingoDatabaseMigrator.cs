using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Rose.Infrastructure.Lingo;

/// <summary>
/// Creates data/lingo.db on first start and applies any new migrations on later starts,
/// before the API begins answering requests.
/// </summary>
internal sealed class LingoDatabaseMigrator(IServiceScopeFactory scopes, ILogger<LingoDatabaseMigrator> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LingoDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0) return;

        logger.LogInformation("Applying ROSE Lingo migrations: {Migrations}", string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
