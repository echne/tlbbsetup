using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class OverallProgressTracker
{
    private int _highWatermark;

    public OverallProgress Current { get; private set; } = OverallProgress.Initial;

    public void Reset()
    {
        _highWatermark = 0;
        Current = OverallProgress.Initial;
    }

    public OverallProgress Restore(int persistedPercent)
    {
        _highWatermark = Math.Clamp(persistedPercent, 0, 99);
        Current = new OverallProgress(_highWatermark, ProgressPhase.Download, "Đã khôi phục tiến trình đã lưu");
        return Current;
    }

    public OverallProgress ReportDownload(double fraction, string message)
    {
        var candidate = (int)Math.Floor(70 * Math.Clamp(fraction, 0, 1));
        return Set(candidate, ProgressPhase.Download, message);
    }

    public OverallProgress ReportPhase(PhaseProgress progress)
    {
        var fraction = progress.ClampedFraction;
        var candidate = progress.Phase switch
        {
            ProgressPhase.Verify => 70 + (int)Math.Floor(5 * fraction),
            ProgressPhase.Extract => 75 + (int)Math.Floor(17 * fraction),
            ProgressPhase.Finalize => 92 + (int)Math.Floor(5 * fraction),
            ProgressPhase.Cleanup => 97 + Math.Min(2, (int)Math.Floor(3 * fraction)),
            ProgressPhase.Complete => 100,
            _ => (int)Math.Floor(70 * fraction)
        };

        return Set(candidate, progress.Phase, progress.Message);
    }

    private OverallProgress Set(int candidate, ProgressPhase phase, string message)
    {
        _highWatermark = Math.Max(_highWatermark, Math.Clamp(candidate, 0, 100));
        Current = new OverallProgress(_highWatermark, phase, message);
        return Current;
    }
}
