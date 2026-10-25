using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IManifestService
{
    Task<LauncherManifest> LoadAsync(CancellationToken cancellationToken = default);
    bool IsTrustedHttpsUrl(Uri? uri);
}

public sealed class LauncherConfigurationException(string message) : Exception(message);
