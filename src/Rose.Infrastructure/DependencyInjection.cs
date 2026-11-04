using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Application.Reminders;
using Rose.Domain.Habits;
using Rose.Domain.Reminders;
using Rose.Domain.Savings;
using Rose.Infrastructure.Importing;
using Rose.Infrastructure.Persistence;
using Rose.Infrastructure.Reminders;

namespace Rose.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Rose";

    public static IServiceCollection AddRoseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReminderOptions>()
            .Bind(configuration.GetSection(ReminderOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<PushOptions>().Bind(configuration.GetSection(PushOptions.Section));

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not set. Locally run 'docker compose up -d' (see README); "
                + "in the cloud set the ConnectionStrings__Rose environment variable.");
        }
        // Retries ride out short network blips between the server and Supabase.
        services.AddDbContextFactory<RoseDbContext>(options =>
            RoseDbContext.Configure(options, connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(nameof(WebPushSender));

        services.AddSingleton<ITrackerWorkbookStore, PostgresTrackerStore>();
        services.AddSingleton<IUploadReader, UploadReader>();
        services.AddSingleton<IRepository<Reminder>, ReminderRepository>();
        services.AddSingleton<IRepository<PushDevice>, PushDeviceRepository>();
        services.AddSingleton<IRepository<SavingsGoal>, SavingsGoalRepository>();
        services.AddSingleton<IRepository<Habit>, HabitRepository>();

        services.AddSingleton(sp => VapidKeys.Resolve(
            sp.GetRequiredService<IOptions<PushOptions>>().Value,
            sp.GetRequiredService<IDbContextFactory<RoseDbContext>>()));
        services.AddSingleton<IPushSender, WebPushSender>();

        return services;
    }
}