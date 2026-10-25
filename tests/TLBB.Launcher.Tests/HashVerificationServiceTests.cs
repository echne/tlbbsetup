using System.Security.Cryptography;
using System.Text;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class HashVerificationServiceTests
{
    [Fact]
    public async Task VerifiesBothLengthAndSha256()
    {
        var root = Path.Combine(Path.GetTempPath(), "pvk-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "volume.bin");
        var bytes = Encoding.UTF8.GetBytes("verified volume");
        await File.WriteAllBytesAsync(path, bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var service = new HashVerificationService();

        Assert.True(await service.VerifyAsync(path, bytes.Length, hash));
        Assert.False(await service.VerifyAsync(path, bytes.Length + 1, hash));
        Assert.False(await service.VerifyAsync(path, bytes.Length, new string('0', 64)));

        Directory.Delete(root, recursive: true);
    }
}
