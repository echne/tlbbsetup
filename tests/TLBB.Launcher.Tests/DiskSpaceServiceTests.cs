using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class DiskSpaceServiceTests
{
    [Fact]
    public void RejectsRelativeAndDriveRootTargets()
    {
        var service = new DiskSpaceService();
        var manifest = ManifestServiceTests.CreateManifest();

        Assert.False(service.ValidateInstallTarget("relative\\game", manifest, Path.GetTempPath()).IsValid);
        Assert.False(service.ValidateInstallTarget(Path.GetPathRoot(Path.GetTempPath())!, manifest, Path.GetTempPath()).IsValid);
    }

    [Fact]
    public void RejectsDiskRequirementsThatOverflow()
    {
        var service = new DiskSpaceService();
        var manifest = ManifestServiceTests.CreateManifest() with
        {
            Archive = ManifestServiceTests.CreateManifest().Archive with { ExpandedSizeBytes = long.MaxValue }
        };
        var target = Path.Combine(Path.GetTempPath(), "pvk-overflow", "game");

        var report = service.ValidateInstallTarget(target, manifest, Path.GetTempPath());

        Assert.False(report.IsValid);
        Assert.Contains("giới hạn", report.Message, StringComparison.OrdinalIgnoreCase);
    }
}
