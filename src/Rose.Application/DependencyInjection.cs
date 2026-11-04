using Microsoft.Extensions.DependencyInjection;
using Rose.Application.Abstractions;
using Rose.Application.Habits;
using Rose.Application.Importing;
using Rose.Application.Notifications;
using Rose.Application.Reminders;
using Rose.Application.Savings;
using Rose.Application.Tracker;
using Rose.Application.Users;

namespace Rose.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRoseApplication(this IServiceCollection services)
    {
        services.AddScoped<PaymentTrackerService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ImportService>();

        // Reminders, savings and habits are cached in memory (one partition per user), so there is exactly one of each.
        services.AddSingleton<ReminderService>();
        services.AddSingleton<PushService>();
        services.AddSingleton<SavingsService>();
        services.AddSingleton<HabitService>();
        foreach (var cache in new[] { typeof(ReminderService), typeof(PushService), typeof(SavingsService), typeof(HabitService) })
        {
            services.AddSingleton(typeof(IUserScopedCache), sp => sp.GetRequiredService(cache));
        }
        services.AddSingleton<UserService>();
        services.AddSingleton<EmailNotifier>();

        // The background scheduler runs every IScheduledJob; each is also resolvable on its own.
        services.AddSingleton<ReminderRinger>();
        services.AddSingleton<IScheduledJob>(sp => sp.GetRequiredService<ReminderRinger>());
        services.AddSingleton<HabitNudger>();
        services.AddSingleton<IScheduledJob>(sp => sp.GetRequiredService<HabitNudger>());
        return services;
    }
}