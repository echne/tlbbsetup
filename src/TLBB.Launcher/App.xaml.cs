using System.Net;
using System.Text.Json;
using System.Windows;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;
using TLBB.Launcher.ViewModels;

namespace TLBB.Launcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (DefenderExclusionCommand.IsInvocation(e.Args))
        {
            Shutdown(DefenderExclusionCommand.Run(e.Args));
            return;
        }

        var options = LoadOptions();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };
        var redirectHandler = new TrustedRedirectHandler(handler, uri =>
            uri is { IsAbsoluteUri: true, Scheme: "https" } &&
            options.TrustedHosts.Any(host => string.Equals(host.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase)));
        var httpClient = new HttpClient(redirectHandler) { Timeout = TimeSpan.FromMinutes(30) };
        var hashService = new HashVerificationService();
        var manifestService = new ManifestService(httpClient, options);
        var settingsService = new SettingsService();
        var archiveService = new SharpCompressArchiveService();
        var shortcutService = new ShortcutService();
        var viewModel = new MainViewModel(
            manifestService,
            new DownloadService(httpClient, hashService, options),
            new InstallationService(archiveService, hashService, shortcutService, options),
            new DiskSpaceService(),
            settingsService,
            new GameLaunchService(),
            new WindowsDefenderExclusionService(),
            new WindowsAntivirusStatusService());

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    private static LauncherOptions LoadOptions()
    {
        try
        {
            using var stream = typeof(App).Assembly.GetManifestResourceStream("TLBB.Launcher.Config.appsettings.json");
            if (stream is not null)
            {
                var options = JsonSerializer.Deserialize<LauncherOptions>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (options is not null)
                {
                    return options with { MaxConcurrentDownloads = Math.Clamp(options.MaxConcurrentDownloads, 1, 3) };
                }
            }
        }
        catch (JsonException) { }

        return new LauncherOptions();
    }
}
