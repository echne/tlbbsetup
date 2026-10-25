namespace TLBB.Launcher.Services;

public interface IGameLaunchService
{
    void Launch(string installDirectory, string relativeExecutablePath);
}
