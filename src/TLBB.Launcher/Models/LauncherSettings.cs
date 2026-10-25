namespace TLBB.Launcher.Models;

public sealed record LauncherSettings
{
    public string InstallPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Games", "TLBBPhongVanKiem");
    public bool CreateDesktopShortcut { get; init; } = true;
    public bool CreateDefenderExclusion { get; init; }
}
