using System.IO;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class DiskSpaceService : IDiskSpaceService
{
    private const long MinimumSafetyMargin = 512L * 1024 * 1024;

    public DiskSpaceReport ValidateInstallTarget(string installPath, LauncherManifest manifest, string stagingPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Path.IsPathFullyQualified(installPath))
        {
            return Invalid("Đường dẫn cài đặt phải là đường dẫn Windows tuyệt đối.");
        }

        string fullInstallPath;
        string fullStagingPath;
        try
        {
            fullInstallPath = Path.GetFullPath(installPath.Trim());
            fullStagingPath = Path.GetFullPath(stagingPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid("Đường dẫn cài đặt không hợp lệ.");
        }

        if (string.Equals(fullInstallPath.TrimEnd(Path.DirectorySeparatorChar),
                Path.GetPathRoot(fullInstallPath)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("Không thể cài game trực tiếp vào thư mục gốc của ổ đĩa.");
        }

        if (File.Exists(fullInstallPath))
        {
            return Invalid("Đường dẫn cài đặt đang là một tệp, không phải thư mục.");
        }

        var archiveBytes = manifest.Archive.Parts.Sum(part => part.Size);
        var expandedBytes = manifest.Archive.ExpandedSizeBytes;
        long stagingRequired;
        long installRequired;
        try
        {
            var archiveMargin = Math.Max(MinimumSafetyMargin, archiveBytes / 10);
            var expandedMargin = Math.Max(MinimumSafetyMargin, expandedBytes / 10);
            stagingRequired = checked(archiveBytes + archiveMargin);
            installRequired = checked(expandedBytes + expandedMargin);
        }
        catch (OverflowException)
        {
            return Invalid("Kích thước archive trong manifest vượt giới hạn hỗ trợ.", archiveBytes, expandedBytes);
        }

        var stagingDrive = FindDrive(fullStagingPath);
        var installDrive = FindDrive(fullInstallPath);
        if (stagingDrive is null || installDrive is null)
        {
            return Invalid("Không xác định được ổ đĩa cho thư mục staging hoặc cài đặt.", archiveBytes, expandedBytes);
        }

        if (string.Equals(stagingDrive.Name, installDrive.Name, StringComparison.OrdinalIgnoreCase))
        {
            var totalRequired = checked(stagingRequired + installRequired);
            if (stagingDrive.AvailableFreeSpace < totalRequired)
            {
                return Invalid($"Ổ {stagingDrive.Name} cần thêm dung lượng trống. Cần khoảng {FormatBytes(totalRequired)}.", archiveBytes, expandedBytes);
            }
        }
        else
        {
            if (stagingDrive.AvailableFreeSpace < stagingRequired)
            {
                return Invalid($"Ổ staging {stagingDrive.Name} cần thêm dung lượng trống. Cần khoảng {FormatBytes(stagingRequired)}.", archiveBytes, expandedBytes);
            }
            if (installDrive.AvailableFreeSpace < installRequired)
            {
                return Invalid($"Ổ cài đặt {installDrive.Name} cần thêm dung lượng trống. Cần khoảng {FormatBytes(installRequired)}.", archiveBytes, expandedBytes);
            }
        }

        var probeDirectory = Path.GetDirectoryName(fullInstallPath);
        while (probeDirectory is not null && !Directory.Exists(probeDirectory))
        {
            probeDirectory = Path.GetDirectoryName(probeDirectory);
        }

        if (probeDirectory is null)
        {
            return Invalid("Không tìm thấy thư mục có sẵn để kiểm tra quyền ghi.", archiveBytes, expandedBytes);
        }

        try
        {
            var probe = Path.Combine(probeDirectory, $".pvk-write-check-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Invalid("Không có quyền ghi vào thư mục cài đặt đã chọn.", archiveBytes, expandedBytes, requiresAdministrator: true);
        }

        return new DiskSpaceReport(true, "Đường dẫn và dung lượng ổ đĩa hợp lệ.", archiveBytes, expandedBytes);
    }

    private static DriveInfo? FindDrive(string path)
    {
        var root = Path.GetPathRoot(path);
        return root is null ? null : DriveInfo.GetDrives().FirstOrDefault(drive =>
            string.Equals(drive.Name, root, StringComparison.OrdinalIgnoreCase) && drive.IsReady);
    }

    private static DiskSpaceReport Invalid(string message, long archiveBytes = 0, long expandedBytes = 0, bool requiresAdministrator = false) =>
        new(false, message, archiveBytes, expandedBytes, requiresAdministrator);

    private static string FormatBytes(long bytes) => $"{bytes / 1024d / 1024 / 1024:0.##} GB";
}
