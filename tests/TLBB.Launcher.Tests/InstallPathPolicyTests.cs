using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class InstallPathPolicyTests
{
    [Fact]
    public void AppendsDedicatedGameFolderToSelectedDirectory()
    {
        var selectedDirectory = Path.Combine(Path.GetTempPath(), "pvk-selected-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(
            Path.Combine(Path.GetFullPath(selectedDirectory), InstallPathPolicy.GameDirectoryName),
            InstallPathPolicy.EnsureGameDirectory(selectedDirectory));
    }

    [Fact]
    public void DoesNotAppendGameFolderTwice()
    {
        var selectedDirectory = Path.Combine(Path.GetTempPath(), "pvk-selected-" + Guid.NewGuid().ToString("N"), "tlbbphongvankiem");

        Assert.Equal(Path.GetFullPath(selectedDirectory), InstallPathPolicy.EnsureGameDirectory(selectedDirectory));
    }
}
