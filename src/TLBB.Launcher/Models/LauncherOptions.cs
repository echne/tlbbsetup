namespace TLBB.Launcher.Models;

public sealed record LauncherOptions
{
    public string ManifestUrl { get; init; } = "";
    public IReadOnlyList<string> TrustedHosts { get; init; } = [];
    public string StagingRoot { get; init; } = "";
    public int MaxConcurrentDownloads { get; init; } = 3;

    public string EffectiveStagingRoot => string.IsNullOrWhiteSpace(StagingRoot)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TLBBPhongVanKiem", "Staging")
        : Environment.ExpandEnvironmentVariables(StagingRoot);
}
