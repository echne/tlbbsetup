using System.Diagnostics;
using System.Management;

namespace TLBB.Launcher.Services;

internal static class DefenderExclusionCommand
{
    private const string NamespacePath = @"\\.\root\Microsoft\Windows\Defender";
    private const string ClassName = "MSFT_MpPreference";
    private const string CommandName = "--add-folder-exclusion";
    private const string VerifyCommandName = "--verify-folder-exclusion";

    public static bool IsInvocation(string[] args) =>
        args.Length > 0 &&
        (string.Equals(args[0], CommandName, StringComparison.Ordinal) ||
         string.Equals(args[0], VerifyCommandName, StringComparison.Ordinal));

    public static int Run(string[] args)
    {
        if (args.Length != 2 || !IsInvocation(args)) return 64;
        if (!DefenderExclusionPathPolicy.TryNormalizeFolder(args[1], out var folderPath, out _)) return 65;

        if (string.Equals(args[0], VerifyCommandName, StringComparison.Ordinal))
        {
            return WaitForExclusionPath(folderPath) ? 0 : 1;
        }

        var addResult = AddFolderExclusionWithErrorMapping(folderPath);
        if (addResult is 0 or 1) return addResult;

        // This helper is already elevated. A fresh child inherits that token and gets a new WMI provider
        // context, matching the successful manual retry without prompting for UAC a second time.
        return MayHaveAppliedExclusion(addResult) && VerifyInFreshElevatedProcess(folderPath) ? 0 : addResult;
    }

    private static int AddFolderExclusionWithErrorMapping(string folderPath)
    {
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

    private static bool MayHaveAppliedExclusion(int result) => result is 70 or 71 or 72 or 73 or (>= 100 and <= 255);

    private static bool VerifyInFreshElevatedProcess(string folderPath)
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(VerifyCommandName);
            startInfo.ArgumentList.Add(folderPath);

            using var verifier = Process.Start(startInfo);
            if (verifier is null) return false;
            if (!verifier.WaitForExit(milliseconds: 7000))
            {
                verifier.Kill(entireProcessTree: true);
                return false;
            }

            return verifier.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool WaitForExclusionPath(string folderPath)
    {
        var timer = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(5);
        while (timer.Elapsed < timeout)
        {
            try
            {
                if (IsExclusionPresent(folderPath)) return true;
            }
            catch (ManagementException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Runtime.InteropServices.COMException) { }

            var remaining = timeout - timer.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            Thread.Sleep(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250));
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
