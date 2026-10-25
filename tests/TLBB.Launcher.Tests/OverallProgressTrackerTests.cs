using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class OverallProgressTrackerTests
{
    [Fact]
    public void MapsStagesToContinuousOverallRanges()
    {
        var tracker = new OverallProgressTracker();

        Assert.Equal(35, tracker.ReportDownload(0.5, "download").Percentage);
        Assert.Equal(70, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Verify, 0, "verify")).Percentage);
        Assert.Equal(75, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Extract, 0, "extract")).Percentage);
        Assert.Equal(92, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Finalize, 0, "finalize")).Percentage);
        Assert.Equal(97, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Cleanup, 0, "cleanup")).Percentage);
    }

    [Fact]
    public void ProgressIsMonotonicAndCleanupDoesNotReachOneHundred()
    {
        var tracker = new OverallProgressTracker();
        var values = new[]
        {
            tracker.ReportDownload(0.6, "downloading").Percentage,
            tracker.ReportDownload(0.3, "retrying").Percentage,
            tracker.ReportPhase(new PhaseProgress(ProgressPhase.Verify, 1, "verified")).Percentage,
            tracker.ReportPhase(new PhaseProgress(ProgressPhase.Extract, 0.7, "extracting")).Percentage,
            tracker.ReportPhase(new PhaseProgress(ProgressPhase.Cleanup, 1, "cleanup" )).Percentage
        };

        Assert.Equal(values.Order().ToArray(), values);
        Assert.Equal(99, values[^1]);
        Assert.Equal(100, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Complete, 1, "complete")).Percentage);
    }

    [Fact]
    public void RestoredProgressDoesNotResetOnRetry()
    {
        var tracker = new OverallProgressTracker();
        tracker.Restore(84);

        Assert.Equal(84, tracker.ReportDownload(0.1, "retry").Percentage);
        Assert.Equal(84, tracker.ReportPhase(new PhaseProgress(ProgressPhase.Extract, 0.1, "resume")).Percentage);
    }

    [Fact]
    public void ResetStartsANewOperationAtZeroAfterCompletion()
    {
        var tracker = new OverallProgressTracker();
        tracker.ReportPhase(new PhaseProgress(ProgressPhase.Complete, 1, "complete"));

        tracker.Reset();

        Assert.Equal(OverallProgress.Initial, tracker.Current);
        Assert.Equal(7, tracker.ReportDownload(0.1, "download").Percentage);
    }
}
