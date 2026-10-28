using Microsoft.Extensions.DependencyInjection;
using Rose.Application.Importing;
using Rose.Application.Reminders;
using Rose.Application.Tracker;

namespace Rose.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddRoseApplication(this IServiceCollection services)
    {
        services.AddScoped<PaymentTrackerService>();
        services.AddScoped<BudgetService>();
        services.AddScoped<ReportService>();
        services.AddScoped<ImportService>();

        // Reminders live in memory, so there is exactly one of each.
        services.AddSingleton<ReminderService>();
        services.AddSingleton<PushService>();
        services.AddSingleton<ReminderRinger>();
        return services;
    }
}