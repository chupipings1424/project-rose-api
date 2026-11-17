using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Application.Notifications;
using Rose.Application.Reminders;
using Rose.Domain.Habits;
using Rose.Domain.Health;
using Rose.Domain.Loans;
using Rose.Domain.Preferences;
using Rose.Domain.Reminders;
using Rose.Domain.Savings;
using Rose.Infrastructure.Accounts;
using Rose.Infrastructure.Email;
using Rose.Infrastructure.Excel;
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
        services.AddOptions<SupabaseAdminOptions>().Bind(configuration.GetSection(SupabaseAdminOptions.Section));
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));

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
        services.AddHttpClient(SupabaseAccountProvider.ClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
        services.AddHttpClient(BrevoEmailSender.ClientName, c => c.Timeout = TimeSpan.FromSeconds(20));

        services.AddSingleton<ITrackerWorkbookStore, PostgresTrackerStore>();
        services.AddSingleton<IUploadReader, UploadReader>();
        services.AddSingleton<IRepository<Reminder>, ReminderRepository>();
        services.AddSingleton<IRepository<PushDevice>, PushDeviceRepository>();
        services.AddSingleton<IRepository<SavingsGoal>, SavingsGoalRepository>();
        services.AddSingleton<IRepository<Habit>, HabitRepository>();
        services.AddSingleton<IRepository<Loan>, LoanRepository>();
        services.AddSingleton<ILoanReportWriter, LoanReportWriter>();
        services.AddSingleton<IRepository<AppPrefs>, AppPrefsRepository>();
        services.AddSingleton<IRepository<HealthProfile>, HealthProfileRepository>();
        services.AddSingleton<IRepository<HealthDay>, HealthDayRepository>();
        services.AddSingleton<IUserStore, PostgresUserStore>();
        services.AddSingleton<IIdempotencyStore, PostgresIdempotencyStore>();
        services.TryAddSingleton<IAccountProvider, SupabaseAccountProvider>();
        services.TryAddSingleton<IEmailSender, BrevoEmailSender>();
        services.AddSingleton(new PublicSite(PublicUrl(configuration)));

        services.AddSingleton(sp => VapidKeys.Resolve(
            sp.GetRequiredService<IOptions<PushOptions>>().Value,
            sp.GetRequiredService<IDbContextFactory<RoseDbContext>>()));
        services.AddSingleton<IPushSender, WebPushSender>();

        return services;
    }

    /// <summary>Email:AppUrl, else the address Render gives the service (RENDER_EXTERNAL_URL).</summary>
    private static Uri? PublicUrl(IConfiguration configuration)
    {
        var url = configuration[$"{EmailOptions.Section}:{nameof(EmailOptions.AppUrl)}"] is { Length: > 0 } configured
            ? configured
            : configuration["RENDER_EXTERNAL_URL"];
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
    }
}