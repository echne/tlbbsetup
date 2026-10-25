using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IArchiveService
{
    Task ExtractSplitZipAsync(
        IReadOnlyList<string> volumePaths,
        string entryPart,
        long expectedExpandedSize,
        string destinationDirectory,
        IProgress<PhaseProgress> progress,
        CancellationToken cancellationToken = default);
}
