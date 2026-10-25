using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class InstallationServiceTests
{
    [Fact]
    public async Task CommitsVerifiedClientAndCleansArchiveAfterSuccess()
    {
        using var workspace = new TestWorkspace("pvk-install");
        var stagingRoot = Path.Combine(workspace.Root, "staging");
        var stagingDirectory = Path.Combine(stagingRoot, "1.0.0");
        Directory.CreateDirectory(stagingDirectory);
        var (manifest, archivePath) = await CreateArchiveAsync(stagingDirectory);
        var installDirectory = Path.Combine(workspace.Root, "game");
        var service = CreateService(stagingRoot, new NoOpShortcutService());

        await service.InstallAsync(manifest, [archivePath], installDirectory, false, new InlineProgress<PhaseProgress>(_ => { }));

        Assert.Equal("game payload", await File.ReadAllTextAsync(Path.Combine(installDirectory, "Game.exe")));
        Assert.True(File.Exists(Path.Combine(installDirectory, ".tlbb-pvk-install.json")));
        Assert.False(File.Exists(Path.Combine(installDirectory, ".tlbb-pvk-transaction.json")));
        Assert.False(File.Exists(archivePath));
        Assert.False(Directory.Exists(stagingDirectory));
    }

    [Fact]
    public async Task DoesNotOverwriteAnUnmanagedNonEmptyDirectory()
    {
        using var workspace = new TestWorkspace("pvk-install-unmanaged");
        var stagingRoot = Path.Combine(workspace.Root, "staging");
        var stagingDirectory = Path.Combine(stagingRoot, "1.0.0");
        Directory.CreateDirectory(stagingDirectory);
        var (manifest, archivePath) = await CreateArchiveAsync(stagingDirectory);
        var installDirectory = Path.Combine(workspace.Root, "game");
        Directory.CreateDirectory(installDirectory);
        var existingFile = Path.Combine(installDirectory, "keep.txt");
        await File.WriteAllTextAsync(existingFile, "user data");
        var service = CreateService(stagingRoot, new NoOpShortcutService());

        await Assert.ThrowsAsync<IOException>(() => service.InstallAsync(
            manifest, [archivePath], installDirectory, false, new InlineProgress<PhaseProgress>(_ => { })));

        Assert.Equal("user data", await File.ReadAllTextAsync(existingFile));
        Assert.False(File.Exists(Path.Combine(installDirectory, "Game.exe")));
    }

    [Fact]
    public async Task ResumesCleanupAfterShortcutFailureWithoutRedownloading()
    {
        using var workspace = new TestWorkspace("pvk-install-resume");
        var stagingRoot = Path.Combine(workspace.Root, "staging");
        var stagingDirectory = Path.Combine(stagingRoot, "1.0.0");
        Directory.CreateDirectory(stagingDirectory);
        var (manifest, archivePath) = await CreateArchiveAsync(stagingDirectory);
        var installDirectory = Path.Combine(workspace.Root, "game");
        var shortcut = new FailOnceShortcutService();
        var service = CreateService(stagingRoot, shortcut);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallAsync(
            manifest, [archivePath], installDirectory, true, new InlineProgress<PhaseProgress>(_ => { })));
        Assert.True(service.HasPendingCommit(installDirectory, manifest.ClientVersion));
        Assert.True(File.Exists(archivePath));
        Assert.True(File.Exists(Path.Combine(installDirectory, "Game.exe")));

        await service.InstallAsync(manifest, [], installDirectory, true, new InlineProgress<PhaseProgress>(_ => { }));

        Assert.False(service.HasPendingCommit(installDirectory, manifest.ClientVersion));
        Assert.False(File.Exists(archivePath));
        Assert.True(File.Exists(Path.Combine(installDirectory, ".tlbb-pvk-install.json")));
        Assert.Equal(2, shortcut.CallCount);
    }

    private static InstallationService CreateService(string stagingRoot, IShortcutService shortcut) =>
        new(new SharpCompressArchiveService(), new HashVerificationService(), shortcut,
            new LauncherOptions { StagingRoot = stagingRoot });

    private static async Task<(LauncherManifest Manifest, string ArchivePath)> CreateArchiveAsync(string stagingDirectory)
    {
        var archivePath = Path.Combine(stagingDirectory, "client.zip");
        var payload = Encoding.UTF8.GetBytes("game payload");
        await using (var file = File.Create(archivePath))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("Game.exe", CompressionLevel.NoCompression);
            await using var stream = entry.Open();
            await stream.WriteAsync(payload);
        }

        var archiveBytes = await File.ReadAllBytesAsync(archivePath);
        var part = new ArchivePart
        {
            Name = Path.GetFileName(archivePath),
            Url = new Uri("https://cdn.example.test/client.zip"),
            Size = archiveBytes.Length,
            Sha256 = Convert.ToHexString(SHA256.HashData(archiveBytes))
        };
        var manifest = new LauncherManifest
        {
            ManifestVersion = 1,
            Game = "test",
            ClientVersion = "1.0.0",
            EntryExecutable = "Game.exe",
            Archive = new ArchiveDescriptor
            {
                Format = "split-zip",
                EntryPart = part.Name,
                ExpandedSizeBytes = payload.Length,
                Parts = [part]
            }
        };
        return (manifest, archivePath);
    }

    private sealed class NoOpShortcutService : IShortcutService
    {
        public void CreateDesktopShortcut(string installDirectory, string relativeExecutablePath) { }
    }

    private sealed class FailOnceShortcutService : IShortcutService
    {
        public int CallCount { get; private set; }
        public void CreateDesktopShortcut(string installDirectory, string relativeExecutablePath)
        {
            CallCount++;
            if (CallCount == 1) throw new InvalidOperationException("Test shortcut failure");
        }
    }
}
