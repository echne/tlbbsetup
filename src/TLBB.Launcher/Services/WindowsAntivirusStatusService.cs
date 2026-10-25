using System.Management;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class WindowsAntivirusStatusService : IAntivirusStatusService
{
    private const string DefenderNamespace = @"\\.\root\Microsoft\Windows\Defender";
    private const string SecurityCenterNamespace = @"\\.\root\SecurityCenter2";

    public async Task<AntivirusStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await Task.Run(ReadStatus, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AntivirusStatus(false, false, []);
        }
    }

    private static AntivirusStatus ReadStatus()
    {
        bool? defenderActive;
        try
        {
            defenderActive = ReadDefenderActive();
        }
        catch
        {
            defenderActive = null;
        }

        IReadOnlyList<string> otherProducts;
        try
        {
            otherProducts = ReadOtherRegisteredProducts();
        }
        catch
        {
            return new AntivirusStatus(false, defenderActive ?? false, []);
        }

        return defenderActive is null
            ? new AntivirusStatus(false, false, otherProducts)
            : new AntivirusStatus(true, defenderActive.Value, otherProducts);
    }

    private static bool? ReadDefenderActive()
    {
        var scope = new ManagementScope(DefenderNamespace);
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus"));
        using var results = searcher.Get();
        foreach (ManagementObject status in results)
        {
            using (status)
            {
                return ToBoolean(status["AMServiceEnabled"]) &&
                       ToBoolean(status["AntivirusEnabled"]) &&
                       ToBoolean(status["RealTimeProtectionEnabled"]);
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ReadOtherRegisteredProducts()
    {
        var scope = new ManagementScope(SecurityCenterNamespace);
        scope.Connect();

        using var searcher = new ManagementObjectSearcher(scope,
            new ObjectQuery("SELECT displayName FROM AntiVirusProduct"));
        using var results = searcher.Get();
        var products = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ManagementObject product in results)
        {
            using (product)
            {
                var name = product["displayName"]?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Defender", StringComparison.OrdinalIgnoreCase))
                {
                    products.Add(name);
                }
            }
        }

        return products.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool ToBoolean(object? value) => value is not null && Convert.ToBoolean(value);
}
