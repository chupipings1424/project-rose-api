using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;
using Rose.Domain.Reminders;

namespace Rose.Infrastructure.Reminders;

/// <summary>How the popup reaches this API again when you press Snooze / Done / Dismiss.</summary>
public interface IAlarmCallback
{
    /// <summary>e.g. http://127.0.0.1:5175</summary>
    string? BaseUrl { get; }

    /// <summary>A short-lived bearer token for the popup's single callback.</summary>
    string CreateToken();
}

/// <summary>
/// The always-on-top Windows popup (ported from the old To-Do &amp; Alarms app). alarm.ps1 draws
/// a WinForms window and reports the button you pressed back to this API.
/// </summary>
public sealed partial class PowerShellDesktopAlarm(
    IOptions<ReminderOptions> options, IAlarmCallback callback, ILogger<PowerShellDesktopAlarm> logger) : IDesktopAlarm
{
    private static readonly Lazy<string> EncodedScript = new(() =>
    {
        using var stream = typeof(PowerShellDesktopAlarm).Assembly.GetManifestResourceStream("Rose.Infrastructure.alarm.ps1")
            ?? throw new InvalidOperationException("alarm.ps1 is not embedded.");
        using var reader = new StreamReader(stream);
        // -EncodedCommand runs the script as a command, so the execution policy can't block it.
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(reader.ReadToEnd()));
    });

    private readonly ConcurrentDictionary<string, Process> open = new();

    public bool Enabled => OperatingSystem.IsWindows() && options.Value.DesktopAlarms;

    public void Show(Reminder reminder, string dueLabel)
    {
        if (!Enabled || open.ContainsKey(reminder.Id)) return;

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-STA", "-WindowStyle", "Hidden", "-EncodedCommand", EncodedScript.Value })
            start.ArgumentList.Add(argument);
        start.Environment["ALARM_TITLE"] = reminder.Text;
        start.Environment["ALARM_DUE"] = dueLabel;
        start.Environment["ALARM_TASKID"] = reminder.Id;
        start.Environment["ALARM_BASEURL"] = callback.BaseUrl ?? "";
        start.Environment["ALARM_TOKEN"] = callback.CreateToken();

        try
        {
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            process.Exited += (_, _) =>
            {
                if (open.TryRemove(reminder.Id, out var done)) done.Dispose();
            };
            open[reminder.Id] = process;
            process.Start();
        }
#pragma warning disable CA1031 // a missing popup must never stop the phones from ringing
        catch (Exception error)
#pragma warning restore CA1031
        {
            if (open.TryRemove(reminder.Id, out var failed)) failed.Dispose();
            LogPopupFailed(error.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not open the desktop alarm: {Reason}")]
    private partial void LogPopupFailed(string reason);
}