using TLBB.Launcher.Infrastructure;

namespace TLBB.Launcher.Tests;

public sealed class LauncherStateMachineTests
{
    [Fact]
    public void AllowsPauseResumeAndCompletionFlow()
    {
        var machine = new LauncherStateMachine();

        Assert.True(machine.TryTransition(LauncherWorkflowState.Downloading));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Paused));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Downloading));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Installing));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Paused));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Downloading));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Installing));
        Assert.True(machine.TryTransition(LauncherWorkflowState.Complete));
        Assert.False(machine.TryTransition(LauncherWorkflowState.Downloading));
    }

    [Fact]
    public void RejectsSkippingRequiredSteps()
    {
        var machine = new LauncherStateMachine();

        Assert.False(machine.TryTransition(LauncherWorkflowState.Complete));
        Assert.Equal(LauncherWorkflowState.Setup, machine.State);
    }

    [Fact]
    public void AllowsReturningFromCompletedInstallToSetup()
    {
        var machine = new LauncherStateMachine();
        machine.TryTransition(LauncherWorkflowState.Downloading);
        machine.TryTransition(LauncherWorkflowState.Installing);
        machine.TryTransition(LauncherWorkflowState.Complete);

        Assert.True(machine.TryTransition(LauncherWorkflowState.Setup));
        Assert.Equal(LauncherWorkflowState.Setup, machine.State);
        Assert.True(machine.TryTransition(LauncherWorkflowState.Downloading));
    }
}
