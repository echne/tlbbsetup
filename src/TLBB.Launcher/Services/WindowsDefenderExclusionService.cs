using System.ComponentModel;
using System.Diagnostics;
using System.Management;

namespace TLBB.Launcher.Services;

public sealed class WindowsDefenderExclusionService : IWindowsDefenderExclusionService
{
    public async Task<DefenderExclusionResult> AddFolderExclusionAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        if (!DefenderExclusionPathPolicy.TryNormalizeFolder(folderPath, out var normalizedPath, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        var launcherPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(launcherPath) || !File.Exists(launcherPath))
        {
            throw new FileNotFoundException("Không tìm thấy file launcher để mở thành phần hỗ trợ Defender.", launcherPath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };
        startInfo.ArgumentList.Add("--add-folder-exclusion");
        startInfo.ArgumentList.Add(normalizedPath);

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Bạn đã hủy yêu cầu quyền Administrator; Defender không được thay đổi.", ex, cancellationToken);
        }

        if (process is null)
        {
            throw new InvalidOperationException("Windows không khởi chạy được thành phần hỗ trợ Defender.");
        }

        using (process)
        {
            await process.WaitForExitAsync(cancellationToken);
            var exitCode = process.ExitCode;
            if (exitCode is 0 or 1)
            {
                return exitCode == 0 ? DefenderExclusionResult.Added : DefenderExclusionResult.AlreadyPresent;
            }

            if (MayHaveAppliedExclusion(exitCode) &&
                await WaitForFolderExclusionAsync(normalizedPath, cancellationToken))
            {
                return DefenderExclusionResult.Added;
            }

            throw exitCode switch
            {
                64 => new InvalidOperationException("Thành phần hỗ trợ nhận tham số không hợp lệ."),
                65 => new InvalidOperationException("Đường dẫn bị từ chối vì không phải thư mục game cục bộ, riêng biệt."),
                70 => new InvalidOperationException("Windows từ chối quyền thay đổi Defender. Hãy xác nhận UAC bằng tài khoản Administrator."),
                71 => new InvalidOperationException("Không thể kết nối tới Microsoft Defender WMI. Defender có thể không hoạt động hoặc máy đang dùng antivirus khác."),
                72 => new InvalidOperationException("Defender không nhận loại trừ này. Cấu hình có thể đang bị Group Policy, Intune hoặc Tamper Protection quản lý."),
                73 => new InvalidOperationException("Defender đã nhận yêu cầu nhưng launcher chưa xác minh được đường dẫn sau 5 giây. Hãy kiểm tra Windows Security trước khi thử lại."),
                >= 100 and <= 255 => new InvalidOperationException($"Defender trả mã WMI {(exitCode - 100)} và launcher chưa xác minh được đường dẫn sau 5 giây. Hãy kiểm tra Windows Security trước khi thử lại."),
                _ => new InvalidOperationException($"Thành phần hỗ trợ Defender kết thúc với mã lỗi {exitCode}.")
            };
        }
    }

    private static bool MayHaveAppliedExclusion(int exitCode) => exitCode is 70 or 71 or 72 or 73 or (>= 100 and <= 255);

    private static async Task<bool> WaitForFolderExclusionAsync(string folderPath, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(5);
        while (timer.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await Task.Run(() => IsFolderExcluded(folderPath), cancellationToken)) return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.COMException) { }

            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            await Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        return false;
    }

    private static bool IsFolderExcluded(string folderPath)
    {
        var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Defender");
        scope.Connect();
        using var defenderClass = new ManagementClass(scope, new ManagementPath("MSFT_MpPreference"), new ObjectGetOptions());
        using var instances = defenderClass.GetInstances();
        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                if (instance["ExclusionPath"] is string[] paths && paths.Any(path => PathsEqual(path, folderPath))) return true;
            }
        }

        return false;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left.Trim().Trim('"'))),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right.Trim().Trim('"'))),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

}
