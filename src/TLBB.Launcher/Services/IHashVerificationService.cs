namespace TLBB.Launcher.Services;

public interface IHashVerificationService
{
    Task<bool> VerifyAsync(string path, long expectedSize, string expectedSha256, CancellationToken cancellationToken = default);
}
