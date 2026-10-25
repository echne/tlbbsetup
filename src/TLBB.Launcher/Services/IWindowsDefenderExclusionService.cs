namespace TLBB.Launcher.Services;

public interface IWindowsDefenderExclusionService
{
    Task<DefenderExclusionResult> AddFolderExclusionAsync(string folderPath, CancellationToken cancellationToken = default);
}

public enum DefenderExclusionResult
{
    Added,
    AlreadyPresent
}
