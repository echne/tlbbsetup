using System.Runtime.InteropServices;

namespace TLBB.Launcher.Services;

public sealed class ShortcutService : IShortcutService
{
    public void CreateDesktopShortcut(string installDirectory, string relativeExecutablePath)
    {
        if (!ManifestService.IsSafeRelativePath(relativeExecutablePath))
        {
            throw new InvalidOperationException("Đường dẫn executable không hợp lệ; không thể tạo shortcut.");
        }

        var executablePath = Path.GetFullPath(Path.Combine(installDirectory, relativeExecutablePath));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Không tìm thấy executable để tạo shortcut.", executablePath);
        }

        var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktopDirectory);
        var shortcutPath = Path.Combine(desktopDirectory, "TLBB Phong Van Kiem.lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("Windows Script Host không khả dụng để tạo shortcut.");
            shell = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("Không thể khởi tạo shortcut service của Windows.");
            dynamic shellObject = shell;
            shortcut = shellObject.CreateShortcut(shortcutPath);
            dynamic shortcutObject = shortcut;
            shortcutObject.TargetPath = executablePath;
            shortcutObject.WorkingDirectory = Path.GetDirectoryName(executablePath)!;
            shortcutObject.Description = "TLBB Phong Vân Kiếm";
            shortcutObject.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
