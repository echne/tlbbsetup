using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IDiskSpaceService
{
    DiskSpaceReport ValidateInstallTarget(string installPath, LauncherManifest manifest, string stagingPath);
}
