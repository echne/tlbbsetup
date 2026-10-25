using System.ComponentModel;
using System.Diagnostics;

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

            throw exitCode switch
            {
                64 => new InvalidOperationException("Thành phần hỗ trợ nhận tham số không hợp lệ."),
                65 => new InvalidOperationException("Đường dẫn bị từ chối vì không phải thư mục game cục bộ, riêng biệt."),
                70 => new InvalidOperationException("Windows từ chối quyền thay đổi Defender. Hãy xác nhận UAC bằng tài khoản Administrator."),
                71 => new InvalidOperationException("Không thể kết nối tới Microsoft Defender WMI. Defender có thể không hoạt động hoặc máy đang dùng antivirus khác."),
                72 => new InvalidOperationException("Sau khi thử lại bằng tiến trình Administrator mới trong tối đa 5 giây, Defender vẫn chưa xác nhận thư mục. Cấu hình có thể bị Group Policy, Intune hoặc Tamper Protection quản lý."),
                73 => new InvalidOperationException("Defender đã nhận yêu cầu nhưng tiến trình Administrator mới không xác nhận được đường dẫn trong tối đa 5 giây. Hãy kiểm tra Windows Security."),
                >= 100 and <= 255 => new InvalidOperationException($"Defender trả mã WMI {(exitCode - 100)}; tiến trình Administrator mới không xác nhận được đường dẫn trong tối đa 5 giây. Hãy kiểm tra Windows Security."),
                _ => new InvalidOperationException($"Thành phần hỗ trợ Defender kết thúc với mã lỗi {exitCode}.")
            };
        }
    }

}
