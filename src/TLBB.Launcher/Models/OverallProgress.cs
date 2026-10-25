namespace TLBB.Launcher.Models;

public enum ProgressPhase
{
    Download,
    Verify,
    Extract,
    Finalize,
    Cleanup,
    Complete
}

public sealed record OverallProgress(int Percentage, ProgressPhase Phase, string Message)
{
    public static OverallProgress Initial { get; } = new(0, ProgressPhase.Download, "Đang chờ bắt đầu");
}

public sealed record DownloadProgress(long BytesReceived, long TotalBytes, string CurrentFile, string Message)
{
    public double Fraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesReceived / TotalBytes, 0, 1);
}

public sealed record PhaseProgress(ProgressPhase Phase, double Fraction, string Message, string? CurrentFile = null)
{
    public double ClampedFraction => Math.Clamp(Fraction, 0, 1);
}
