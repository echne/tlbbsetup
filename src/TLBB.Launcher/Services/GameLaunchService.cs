using System.Diagnostics;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    public void Launch(string installDirectory, string relativeExecutablePath)
    {
        if (!ManifestService.IsSafeRelativePath(relativeExecutablePath))
        {
            throw new InvalidOperationException("Đường dẫn executable không hợp lệ; hãy thử repair game.");
        }

        var root = Path.GetFullPath(installDirectory);
        var executable = Path.GetFullPath(Path.Combine(root, relativeExecutablePath));
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
        {
            throw new FileNotFoundException("Không tìm thấy executable trong thư mục game. Hãy thử repair game.", executable);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = true
        });
    }
}
