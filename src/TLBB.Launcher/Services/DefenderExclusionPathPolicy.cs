namespace TLBB.Launcher.Services;

public static class DefenderExclusionPathPolicy
{
    public static bool TryNormalizeFolder(string? path, out string normalizedPath, out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
        {
            errorMessage = "Nhập hoặc chọn đường dẫn thư mục game đầy đủ trước.";
            return false;
        }

        var value = path.Trim();
        if (value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            errorMessage = "Chỉ hỗ trợ thư mục cài đặt trên ổ đĩa cục bộ.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            var comparisonPath = Path.TrimEndingDirectorySeparator(fullPath);
            var comparisonRoot = root is null ? string.Empty : Path.TrimEndingDirectorySeparator(root);
            if (string.Equals(comparisonPath, comparisonRoot, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Không thể loại trừ cả ổ đĩa. Hãy chọn một thư mục con dành riêng cho game.";
                return false;
            }

            var protectedFolders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };

            if (protectedFolders.Where(folder => !string.IsNullOrWhiteSpace(folder))
                .Any(folder => IsSameOrAncestorOf(fullPath, Path.GetFullPath(folder))))
            {
                errorMessage = "Thư mục này quá rộng hoặc là thư mục hệ thống. Hãy chọn một thư mục con riêng cho game.";
                return false;
            }

            normalizedPath = comparisonPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            errorMessage = "Đường dẫn thư mục cài đặt không hợp lệ.";
            return false;
        }
    }

    private static bool IsSameOrAncestorOf(string candidate, string protectedPath)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedProtected = Path.GetFullPath(protectedPath);
        if (string.Equals(normalizedCandidate, Path.TrimEndingDirectorySeparator(normalizedProtected), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(normalizedCandidate)
            ? normalizedCandidate
            : normalizedCandidate + Path.DirectorySeparatorChar;
        return normalizedProtected.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
