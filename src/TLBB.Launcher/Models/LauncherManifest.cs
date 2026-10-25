namespace TLBB.Launcher.Models;

public sealed record LauncherManifest
{
    public int ManifestVersion { get; init; }
    public string Game { get; init; } = "";
    public string ClientVersion { get; init; } = "";
    public string EntryExecutable { get; init; } = "";
    public Uri? RegisterUrl { get; init; }
    public ArchiveDescriptor Archive { get; init; } = new();
}

public sealed record ArchiveDescriptor
{
    public string Format { get; init; } = "";
    public string EntryPart { get; init; } = "";
    public long ExpandedSizeBytes { get; init; }
    public IReadOnlyList<ArchivePart> Parts { get; init; } = [];
}

public sealed record ArchivePart
{
    public string Name { get; init; } = "";
    public Uri Url { get; init; } = new("https://invalid.invalid/");
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
}
