namespace TLBB.Launcher.Services;

public interface IShortcutService
{
    void CreateDesktopShortcut(string installDirectory, string relativeExecutablePath);
}
