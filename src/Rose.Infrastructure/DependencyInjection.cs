using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Application.Reminders;
using Rose.Domain.Reminders;
using Rose.Infrastructure.Excel;
using Rose.Infrastructure.Importing;
using Rose.Infrastructure.Reminders;
using Rose.Infrastructure.Storage;

namespace Rose.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRoseInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RoseOptions>()
            .Bind(configuration.GetSection(RoseOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart()
            .PostConfigure<IHostEnvironment>((options, env) =>
            {
                // Relative paths are relative to the API project, not to wherever dotnet was started.
                options.DataDirectory = Path.GetFullPath(Path.Combine(env.ContentRootPath, options.DataDirectory));
                if (options.WorkbookPath is { Length: > 0 } workbook)
                    options.WorkbookPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, workbook));
            });
        services.AddOptions<ReminderOptions>()
            .Bind(configuration.GetSection(ReminderOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(nameof(WebPushSender));

        services.AddSingleton<ITrackerWorkbookStore, ClosedXmlTrackerWorkbookStore>();
        services.AddSingleton<IUploadReader, UploadReader>();

        services.AddSingleton<IDataFile<List<Reminder>>>(sp =>
            new JsonDataFile<List<Reminder>>(DataFile(sp, "reminders.json"), () => []));
        services.AddSingleton<IDataFile<List<PushDevice>>>(sp =>
            new JsonDataFile<List<PushDevice>>(DataFile(sp, "push-devices.json"), () => []));
        services.AddSingleton(sp =>
            VapidKeys.LoadOrCreate(new JsonDataFile<VapidKeys?>(DataFile(sp, "vapid.json"), () => null)));
        services.AddSingleton<IPushSender, WebPushSender>();
        services.AddSingleton<IDesktopAlarm, PowerShellDesktopAlarm>();
        return services;
    }

    private static string DataFile(IServiceProvider services, string name) =>
        Path.Combine(services.GetRequiredService<IOptions<RoseOptions>>().Value.DataDirectory, name);
}