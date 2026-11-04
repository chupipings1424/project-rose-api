using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Rose.Infrastructure.Persistence;

/// <summary>
/// ROSE's tables, all in the <c>rose</c> schema with snake_case names. The schema keeps them
/// out of Supabase's public Data API; only this server (the table owner) can reach them.
/// </summary>
public sealed class RoseDbContext(DbContextOptions<RoseDbContext> options) : DbContext(options)
{
    public const string Schema = "rose";

    internal DbSet<AccountRow> Accounts => Set<AccountRow>();
    internal DbSet<PaymentRow> Payments => Set<PaymentRow>();
    internal DbSet<BudgetLineRow> BudgetLines => Set<BudgetLineRow>();
    internal DbSet<ReminderRow> Reminders => Set<ReminderRow>();
    internal DbSet<PushDeviceRow> PushDevices => Set<PushDeviceRow>();
    internal DbSet<SavingsGoalRow> SavingsGoals => Set<SavingsGoalRow>();
    internal DbSet<HabitRow> Habits => Set<HabitRow>();
    internal DbSet<AppSecretRow> AppSecrets => Set<AppSecretRow>();
    internal DbSet<UserRow> Users => Set<UserRow>();

    /// <summary>The one place the provider is configured (app, tests and design-time tools).</summary>
    public static void Configure(DbContextOptionsBuilder options, string connectionString, Action<NpgsqlDbContextOptionsBuilder>? npgsql = null) =>
        options
            .UseNpgsql(connectionString, o =>
            {
                o.MigrationsHistoryTable("__ef_migrations_history", Schema);
                npgsql?.Invoke(o);
            })
            .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AccountRow>(e =>
        {
            e.ToTable("accounts");
            e.HasKey(x => x.Seq);
            e.Property(x => x.Seq).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<PaymentRow>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.OwnerId, x.Title });
        });

        modelBuilder.Entity<BudgetLineRow>(e =>
        {
            e.ToTable("budget_lines");
            e.HasKey(x => x.Id);
            e.Property(x => x.Seq).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.OwnerId, x.Year });
        });

        modelBuilder.Entity<ReminderRow>(e =>
        {
            e.ToTable("reminders");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<PushDeviceRow>(e =>
        {
            e.ToTable("push_devices");
            e.HasKey(x => x.Endpoint);
            e.HasIndex(x => x.OwnerId);
        });

        modelBuilder.Entity<SavingsGoalRow>(e =>
        {
            e.ToTable("savings_goals");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Target).HasColumnType("numeric");
            e.HasMany(x => x.Entries).WithOne().HasForeignKey(x => x.GoalId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SavingsEntryRow>(e =>
        {
            e.ToTable("savings_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasColumnType("numeric");
        });

        modelBuilder.Entity<HabitRow>(e =>
        {
            e.ToTable("habits");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OwnerId);
            e.HasMany(x => x.Days).WithOne().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HabitDayRow>(e =>
        {
            e.ToTable("habit_days");
            e.HasKey(x => new { x.HabitId, x.Date });
        });

        modelBuilder.Entity<UserRow>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<AppSecretRow>(e =>
        {
            e.ToTable("app_secrets");
            e.HasKey(x => x.Key);
        });
    }
}