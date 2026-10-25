namespace TLBB.Launcher.Models;

public sealed record InstallationState
{
    public int StateVersion { get; init; } = 1;
    public string ClientVersion { get; init; } = "";
    public string TargetDirectory { get; init; } = "";
    public string Status { get; init; } = "Downloading";
    public int LastOverallProgress { get; init; }
    public IReadOnlyList<string> VerifiedParts { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
