using System.Management;

namespace TLBB.Launcher.DefenderHelper;

internal static class Program
{
    private const string NamespacePath = @"\\.\root\Microsoft\Windows\Defender";
    private const string ClassName = "MSFT_MpPreference";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || !string.Equals(args[0], "--add-folder-exclusion", StringComparison.Ordinal))
        {
            return 64;
        }

        if (!TryNormalizeGameFolder(args[1], out var folderPath))
        {
            return 65;
        }

        try
        {
            return AddFolderExclusion(folderPath);
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.AccessDenied)
        {
            return 70;
        }
        catch (UnauthorizedAccessException)
        {
            return 70;
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace or ManagementStatus.NotFound)
        {
            return 71;
        }
        catch (ManagementException)
        {
            return 72;
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80041003))
        {
            return 70;
        }
        catch
        {
            return 72;
        }
    }

    private static int AddFolderExclusion(string folderPath)
    {
        uint returnCode;
        {
            var scope = new ManagementScope(NamespacePath);
            scope.Connect();

            using var defenderClass = new ManagementClass(scope, new ManagementPath(ClassName), new ObjectGetOptions());
            if (ReadExclusionPaths(defenderClass).Any(path => PathsEqual(path, folderPath)))
            {
                return 1;
            }

            using var input = defenderClass.GetMethodParameters("Add");
            input["ExclusionPath"] = new[] { folderPath };
            input["ExclusionExtension"] = Array.Empty<string>();
            input["ExclusionProcess"] = Array.Empty<string>();
            input["ThreatIDDefaultAction_Ids"] = Array.Empty<long>();
            input["ThreatIDDefaultAction_Actions"] = Array.Empty<byte>();
            input["Force"] = true;

            using var output = defenderClass.InvokeMethod("Add", input, null);
            var returnValue = output?["ReturnValue"];
            if (returnValue is null)
            {
                return 72;
            }

            returnCode = Convert.ToUInt32(returnValue);
        }

        // A successful WMI response is authoritative. When it reports a nonzero result, poll fresh WMI
        // connections because the same ManagementClass instance can keep returning a stale preference list.
        if (returnCode == 0)
        {
            return 0;
        }

        return WaitForExclusionPath(folderPath) ? 0 : EncodeWmiFailure(returnCode);
    }

    private static bool WaitForExclusionPath(string folderPath)
    {
        const int attempts = 20;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            if (IsExclusionPresent(folderPath))
            {
                return true;
            }

            if (attempt + 1 < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(250));
            }
        }

        return false;
    }

    private static bool IsExclusionPresent(string folderPath)
    {
        var scope = new ManagementScope(NamespacePath);
        scope.Connect();
        using var defenderClass = new ManagementClass(scope, new ManagementPath(ClassName), new ObjectGetOptions());
        return ReadExclusionPaths(defenderClass).Any(path => PathsEqual(path, folderPath));
    }

    private static int EncodeWmiFailure(uint returnCode) => returnCode <= 155 ? 100 + (int)returnCode : 73;

    private static IEnumerable<string> ReadExclusionPaths(ManagementClass defenderClass)
    {
        using var instances = defenderClass.GetInstances();
        foreach (ManagementObject instance in instances)
        {
            using (instance)
            {
                if (instance["ExclusionPath"] is string[] paths)
                {
                    foreach (var path in paths)
                    {
                        yield return path;
                    }
                }
            }
        }
    }

    private static bool TryNormalizeGameFolder(string value, out string folderPath)
    {
        folderPath = string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value) ||
            value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            var root = Path.GetPathRoot(fullPath);
            var normalizedPath = Path.TrimEndingDirectorySeparator(fullPath);
            if (root is null || string.Equals(normalizedPath, Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase))
            {
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

            if (protectedFolders.Where(path => !string.IsNullOrWhiteSpace(path))
                .Any(path => IsSameOrAncestor(normalizedPath, Path.GetFullPath(path))))
            {
                return false;
            }

            folderPath = normalizedPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsSameOrAncestor(string candidate, string protectedPath)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedProtected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(protectedPath));
        if (string.Equals(normalizedCandidate, normalizedProtected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(normalizedCandidate)
            ? normalizedCandidate
            : normalizedCandidate + Path.DirectorySeparatorChar;
        return protectedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
