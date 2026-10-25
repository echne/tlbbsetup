using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface ISettingsService
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
    Task<InstallationState?> LoadInstallationStateAsync(CancellationToken cancellationToken = default);
    Task SaveInstallationStateAsync(InstallationState state, CancellationToken cancellationToken = default);
    string StateDirectory { get; }
}
