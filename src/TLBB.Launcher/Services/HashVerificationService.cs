using System.Security.Cryptography;

namespace TLBB.Launcher.Services;

public sealed class HashVerificationService : IHashVerificationService
{
    public async Task<bool> VerifyAsync(string path, long expectedSize, string expectedSha256, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length != expectedSize)
        {
            return false;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        try
        {
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedSha256));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
