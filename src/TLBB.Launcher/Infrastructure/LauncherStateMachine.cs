namespace TLBB.Launcher.Infrastructure;

public enum LauncherWorkflowState
{
    Setup,
    Downloading,
    Paused,
    Installing,
    Failed,
    Complete
}

public sealed class LauncherStateMachine
{
    public LauncherWorkflowState State { get; private set; } = LauncherWorkflowState.Setup;

    public bool TryTransition(LauncherWorkflowState next)
    {
        var allowed = (State, next) switch
        {
            (LauncherWorkflowState.Setup, LauncherWorkflowState.Downloading) => true,
            (LauncherWorkflowState.Downloading, LauncherWorkflowState.Paused or LauncherWorkflowState.Installing or LauncherWorkflowState.Failed or LauncherWorkflowState.Setup) => true,
            (LauncherWorkflowState.Paused, LauncherWorkflowState.Downloading or LauncherWorkflowState.Setup) => true,
            (LauncherWorkflowState.Installing, LauncherWorkflowState.Paused or LauncherWorkflowState.Complete or LauncherWorkflowState.Failed or LauncherWorkflowState.Setup) => true,
            (LauncherWorkflowState.Failed, LauncherWorkflowState.Downloading or LauncherWorkflowState.Setup) => true,
            (LauncherWorkflowState.Complete, LauncherWorkflowState.Setup) => true,
            _ => false
        };

        if (allowed)
        {
            State = next;
        }

        return allowed;
    }
}
