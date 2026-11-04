using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Rose.Infrastructure.Persistence;

/// <summary>Brings the database up to date when the server starts.</summary>
public static class RoseDatabase
{
    /// <summary>
    /// Defence in depth for Supabase: the <c>rose</c> schema is not exposed by its Data API, but
    /// if it ever were, row level security without policies plus revoked grants means the public
    /// <c>anon</c> / <c>authenticated</c> roles still see nothing. This server connects as the
    /// table owner, which RLS does not restrict. Runs after every migration, so new tables are covered.
    /// </summary>
    private const string LockdownSql = """
        DO $$
        DECLARE t record;
        BEGIN
          FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'rose' LOOP
            EXECUTE format('ALTER TABLE rose.%I ENABLE ROW LEVEL SECURITY', t.tablename);
          END LOOP;
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
            REVOKE ALL ON SCHEMA rose FROM anon;
            REVOKE ALL ON ALL TABLES IN SCHEMA rose FROM anon;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA rose FROM anon;
          END IF;
          IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
            REVOKE ALL ON SCHEMA rose FROM authenticated;
            REVOKE ALL ON ALL TABLES IN SCHEMA rose FROM authenticated;
            REVOKE ALL ON ALL SEQUENCES IN SCHEMA rose FROM authenticated;
          END IF;
        END $$;
        """;

    /// <summary>
    /// Applies pending migrations (creating the database if needed), then locks the schema down.
    /// Retries a few times, because the first connection after a cold start (Supabase pooler,
    /// Docker) can drop, and EF reads the migration history outside its own retry strategy.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var contexts = services.GetRequiredService<IDbContextFactory<RoseDbContext>>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var db = await contexts.CreateDbContextAsync(cancellationToken);
                await db.Database.MigrateAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync(LockdownSql, cancellationToken);
                return;
            }
            catch (NpgsqlException error) when (error.IsTransient && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }
    }
}

/// <summary>
/// <c>/health</c> runs a real query, so an uptime pinger also keeps the Supabase project active
/// (free projects pause after a week without database activity).
/// </summary>
internal sealed class DatabaseHealthCheck(IDbContextFactory<RoseDbContext> contexts) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await contexts.CreateDbContextAsync(cancellationToken);
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Database unreachable.");
        }
#pragma warning disable CA1031 // a health check reports failures instead of throwing
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Database unreachable.", error);
        }
    }
}