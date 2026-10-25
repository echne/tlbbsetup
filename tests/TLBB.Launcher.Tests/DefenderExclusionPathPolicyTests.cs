using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class DefenderExclusionPathPolicyTests
{
    [Fact]
    public void RejectsDriveRoot()
    {
        var driveRoot = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.False(DefenderExclusionPathPolicy.TryNormalizeFolder(driveRoot, out _, out var message));
        Assert.Contains("cả ổ đĩa", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcceptsDedicatedGameSubfolder()
    {
        var gameFolder = Path.Combine(Path.GetTempPath(), $"PVK-{Guid.NewGuid():N}");

        Assert.True(DefenderExclusionPathPolicy.TryNormalizeFolder(gameFolder, out var normalized, out var message), message);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder)), normalized);
    }

    [Fact]
    public void RejectsWindowsAndBroadSystemDirectories()
    {
        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.False(DefenderExclusionPathPolicy.TryNormalizeFolder(windowsDirectory, out _, out _));
    }

    [Fact]
    public void RejectsNetworkAndRelativePaths()
    {
        Assert.False(DefenderExclusionPathPolicy.TryNormalizeFolder(@"\\server\share\PVK", out _, out _));
        Assert.False(DefenderExclusionPathPolicy.TryNormalizeFolder("Games\\PVK", out _, out _));
    }
}
