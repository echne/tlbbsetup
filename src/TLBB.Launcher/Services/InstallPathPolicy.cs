namespace TLBB.Launcher.Services;

public static class InstallPathPolicy
{
    public const string GameDirectoryName = "TLBBPhongVanKiem";

    public static string EnsureGameDirectory(string selectedDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedDirectory);
        var fullPath = Path.GetFullPath(selectedDirectory.Trim());
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(fullPath));
        return string.Equals(folderName, GameDirectoryName, StringComparison.OrdinalIgnoreCase)
            ? fullPath
            : Path.Combine(fullPath, GameDirectoryName);
    }
}
