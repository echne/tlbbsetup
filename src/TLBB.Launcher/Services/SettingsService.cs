using System.Text.Json;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class SettingsService : ISettingsService
{
    private const int CurrentSettingsVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _settingsPath;
    private readonly string _statePath;

    public SettingsService(string? stateDirectory = null)
    {
        StateDirectory = stateDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TLBBPhongVanKiem");
        Directory.CreateDirectory(StateDirectory);
        _settingsPath = Path.Combine(StateDirectory, "settings.json");
        _statePath = Path.Combine(StateDirectory, "installation-state.json");
    }

    public string StateDirectory { get; }

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_settingsPath))
        {
            return new LauncherSettings { InstallPath = @"C:\Games\TLBBPhongVanKiem" };
        }

        try
        {
            await using var stream = new FileStream(_settingsPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new LauncherSettings { InstallPath = @"C:\Games\TLBBPhongVanKiem" };
            }

            var settings = document.RootElement.Deserialize<LauncherSettings>(JsonOptions) ?? new LauncherSettings();
            var versionProperty = document.RootElement.EnumerateObject()
                .FirstOrDefault(property => string.Equals(property.Name, nameof(LauncherSettings.SettingsVersion), StringComparison.OrdinalIgnoreCase));
            var savedVersion = versionProperty.Value.ValueKind == JsonValueKind.Number && versionProperty.Value.TryGetInt32(out var version)
                ? version
                : 0;

            if (savedVersion < CurrentSettingsVersion)
            {
                // Older versions defaulted this option to off. Migrate in memory during startup, but
                // defer writing until the next settings save so a read-only settings file cannot close the app.
                settings = settings with
                {
                    SettingsVersion = CurrentSettingsVersion,
                    CreateDefenderExclusion = true
                };
            }

            return settings;
        }
        catch (JsonException)
        {
            return new LauncherSettings { InstallPath = @"C:\Games\TLBBPhongVanKiem" };
        }
    }

    public Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(_settingsPath, settings with { SettingsVersion = CurrentSettingsVersion }, cancellationToken);

    public async Task<InstallationState?> LoadInstallationStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_statePath))
        {
            return null;
        }

        try
        {
            var state = await ReadAsync<InstallationState>(_statePath, cancellationToken);
            return state?.StateVersion == 1 ? state : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveInstallationStateAsync(InstallationState state, CancellationToken cancellationToken = default) =>
        WriteAtomicAsync(_statePath, state with { UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);

    private static async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
