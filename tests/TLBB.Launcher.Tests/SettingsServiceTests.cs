using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task SavesAndRestoresSettingsAndInstallationState()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvk-settings-" + Guid.NewGuid().ToString("N"));
        var service = new SettingsService(root);
        var settings = new LauncherSettings
        {
            InstallPath = Path.Combine(root, "game"),
            CreateDesktopShortcut = false,
            CreateDefenderExclusion = true
        };
        var state = new InstallationState
        {
            ClientVersion = "1.0.0",
            TargetDirectory = settings.InstallPath,
            Status = "Paused",
            LastOverallProgress = 52,
            VerifiedParts = ["client.z01"]
        };

        await service.SaveAsync(settings);
        await service.SaveInstallationStateAsync(state);

        Assert.Equal(settings, await service.LoadAsync());
        Assert.Equal(state.ClientVersion, (await service.LoadInstallationStateAsync())?.ClientVersion);
        Assert.Equal(52, (await service.LoadInstallationStateAsync())?.LastOverallProgress);

        Directory.Delete(root, recursive: true);
    }
}
