using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IDownloadService
{
    Task<IReadOnlyList<string>> DownloadAndVerifyAsync(
        LauncherManifest manifest,
        string stagingDirectory,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default);
}
