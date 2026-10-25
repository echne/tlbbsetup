namespace TLBB.Launcher.Models;

public sealed record LauncherSettings
{
    public int SettingsVersion { get; init; } = 1;
    public string InstallPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Games", "TLBBPhongVanKiem");
    public bool CreateDesktopShortcut { get; init; } = true;
    public bool CreateDefenderExclusion { get; init; } = true;
}
