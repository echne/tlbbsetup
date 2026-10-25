using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public interface IAntivirusStatusService
{
    Task<AntivirusStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
