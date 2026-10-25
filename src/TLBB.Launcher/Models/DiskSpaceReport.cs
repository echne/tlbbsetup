namespace TLBB.Launcher.Models;

public sealed record DiskSpaceReport(
    bool IsValid,
    string Message,
    long ArchiveBytesRequired,
    long ExpandedBytesRequired,
    bool RequiresAdministrator = false);
