using System.Management;

namespace TLBB.Launcher.Services;

internal static class DefenderExclusionCommand
{
    private const string NamespacePath = @"\\.\root\Microsoft\Windows\Defender";
    private const string ClassName = "MSFT_MpPreference";
    private const string CommandName = "--add-folder-exclusion";

    public static bool IsInvocation(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.Ordinal);

    public static int Run(string[] args)
    {
        if (args.Length != 2 || !IsInvocation(args)) return 64;
        if (!DefenderExclusionPathPolicy.TryNormalizeFolder(args[1], out var folderPath, out _)) return 65;

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
        var scope = new ManagementScope(NamespacePath);
        scope.Connect();

        using var defenderClass = new ManagementClass(scope, new ManagementPath(ClassName), new ObjectGetOptions());
        if (ReadExclusionPaths(defenderClass).Any(path => PathsEqual(path, folderPath))) return 1;

        using var input = defenderClass.GetMethodParameters("Add");
        input["ExclusionPath"] = new[] { folderPath };
        input["ExclusionExtension"] = Array.Empty<string>();
        input["ExclusionProcess"] = Array.Empty<string>();
        input["ThreatIDDefaultAction_Ids"] = Array.Empty<long>();
        input["ThreatIDDefaultAction_Actions"] = Array.Empty<byte>();
        input["Force"] = true;

        using var output = defenderClass.InvokeMethod("Add", input, null);
        var returnValue = output?["ReturnValue"];
        if (returnValue is null) return 72;
        var returnCode = Convert.ToUInt32(returnValue);
        return returnCode == 0 ? 0 : EncodeWmiFailure(returnCode);
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
                    foreach (var path in paths) yield return path;
                }
            }
        }
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
