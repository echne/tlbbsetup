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
            return process.ExitCode switch
            {
                0 => DefenderExclusionResult.Added,
                1 => DefenderExclusionResult.AlreadyPresent,
                64 => throw new InvalidOperationException("Thành phần hỗ trợ nhận tham số không hợp lệ."),
                65 => throw new InvalidOperationException("Đường dẫn bị từ chối vì không phải thư mục game cục bộ, riêng biệt."),
                70 => throw new InvalidOperationException("Windows từ chối quyền thay đổi Defender. Hãy xác nhận UAC bằng tài khoản Administrator."),
                71 => throw new InvalidOperationException("Không thể kết nối tới Microsoft Defender WMI. Defender có thể không hoạt động hoặc máy đang dùng antivirus khác."),
                72 => throw new InvalidOperationException("Defender không nhận loại trừ này. Cấu hình có thể đang bị Group Policy, Intune hoặc Tamper Protection quản lý."),
                _ => throw new InvalidOperationException($"Thành phần hỗ trợ Defender kết thúc với mã lỗi {process.ExitCode}.")
            };
        }
    }

}
