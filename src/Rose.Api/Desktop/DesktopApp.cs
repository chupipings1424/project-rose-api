using System.Diagnostics;
using Microsoft.Extensions.FileProviders;

namespace Rose.Api.Desktop;

/// <summary>
/// ROSE.exe - the packaged build (package-rose.cmd in the UI repo). It embeds the built
/// React app, so one file serves the API and the web pages on the same origin. When
/// present, this class keeps ROSE to a single copy, opens the browser and reports
/// startup errors (a WinExe has no console to print them on).
/// </summary>
internal sealed class DesktopApp : IDisposable
{
    private const string MutexName = @"Local\ProjectROSE";
    private const string ManifestName = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";

    private readonly Mutex instance;
    private readonly string url;
    private readonly bool openBrowser;

    public DesktopApp(IConfiguration configuration)
    {
        instance = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        AlreadyRunning = !createdNew;
        url = BrowserUrl(configuration["Urls"]);
        openBrowser = configuration.GetValue("Desktop:OpenBrowser", defaultValue: true);
    }

    /// <summary>True when this build carries the React UI inside it (only ROSE.exe does).</summary>
    public static bool HasEmbeddedUi { get; } =
        typeof(DesktopApp).Assembly.GetManifestResourceNames().Contains(ManifestName);

    /// <summary>Another ROSE.exe already owns the port: just show its page.</summary>
    public bool AlreadyRunning { get; }

    /// <summary>The embedded React build (project-rose/dist), as files for UseStaticFiles.</summary>
    public static IFileProvider UiFiles()
    {
        // A single-file app has no Assembly.Location, so give the files the exe's own timestamp.
        var stamp = Environment.ProcessPath is { } exe ? File.GetLastWriteTimeUtc(exe) : DateTime.UtcNow;
        return new ManifestEmbeddedFileProvider(typeof(DesktopApp).Assembly, "wwwroot", new DateTimeOffset(stamp));
    }

    /// <summary>The address to open: the first configured URL, with "listen everywhere" hosts made browsable.</summary>
    public static string BrowserUrl(string? urls)
    {
        var first = (urls ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? "http://127.0.0.1:5000";
        return first.Replace("://*", "://localhost", StringComparison.Ordinal)
            .Replace("://+", "://localhost", StringComparison.Ordinal)
            .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
            .Replace("://[::]", "://localhost", StringComparison.Ordinal)
            .TrimEnd('/') + "/";
    }

    /// <summary>Shows ROSE in the default browser (skipped with --Desktop:OpenBrowser=false).</summary>
    public void OpenBrowser()
    {
        if (!openBrowser) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // no default browser: ROSE still runs, the address is in rose-error.log
            WriteError($"Could not open a browser. Go to {url} yourself.");
        }
    }

    /// <summary>Runs ROSE, opens the browser once it listens, and explains any crash in rose-error.log.</summary>
    public async Task RunAsync(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(OpenBrowser);
        try
        {
            await app.RunAsync();
        }
        catch (Exception ex)
        {
            var log = WriteError($"ROSE could not start or stopped unexpectedly:{Environment.NewLine}{ex}");
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"") { UseShellExecute = true })?.Dispose();
            throw;
        }
    }

    private static string WriteError(string message)
    {
        var log = Path.Combine(AppContext.BaseDirectory, "rose-error.log");
        File.WriteAllText(log, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{message}{Environment.NewLine}");
        return log;
    }

    // The process is ending: let Windows release the mutex (ReleaseMutex must run on the
    // thread that took it, which an async Main cannot promise).
    public void Dispose() => instance.Dispose();
}