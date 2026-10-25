using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IInstallationService
{
    bool HasPendingCommit(string installDirectory, string clientVersion);

    Task InstallAsync(
        LauncherManifest manifest,
        IReadOnlyList<string> verifiedParts,
        string installDirectory,
        bool createDesktopShortcut,
        IProgress<PhaseProgress> progress,
        CancellationToken cancellationToken = default);
}
