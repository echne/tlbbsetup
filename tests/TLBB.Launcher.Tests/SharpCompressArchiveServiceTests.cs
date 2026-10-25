using SharpCompress.Archives.Zip;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class SharpCompressArchiveServiceTests
{
    [Fact]
    public async Task ExtractsUpstreamZ01ZipFixtureThroughMultiVolumeArchiveApi()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "TestData");
        var zipPath = Path.Combine(fixtureRoot, "WinZip26.nocomp.multi.zip");
        var firstPartPath = Path.Combine(fixtureRoot, "WinZip26.nocomp.multi.z01");
        using var archive = ZipArchive.OpenArchive([new FileInfo(zipPath), new FileInfo(firstPartPath)]);
        var expectedSize = archive.Entries.Where(entry => !entry.IsDirectory).Sum(entry => entry.Size);
        Assert.True(expectedSize > 0);

        using var workspace = new TestWorkspace("pvk-split-zip");
        var destination = Path.Combine(workspace.Root, "extracted");
        var progressValues = new List<PhaseProgress>();
        var service = new SharpCompressArchiveService();

        await service.ExtractSplitZipAsync(
            [zipPath, firstPartPath],
            Path.GetFileName(zipPath),
            expectedSize,
            destination,
            new InlineProgress<PhaseProgress>(progressValues.Add));

        var outputFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(outputFiles);
        Assert.Equal(expectedSize, outputFiles.Sum(path => new FileInfo(path).Length));
        Assert.Equal(1, (int)progressValues.Last().Fraction);
    }

    [Fact]
    public async Task RejectsZipSlipBeforeWritingOutsideStaging()
    {
        using var workspace = new TestWorkspace("pvk-zip-slip");
        var zipPath = Path.Combine(workspace.Root, "malicious.zip");
        await using (var file = File.Create(zipPath))
        using (var archive = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../outside.txt");
            await using var content = entry.Open();
            await content.WriteAsync("bad"u8.ToArray());
        }

        var destination = Path.Combine(workspace.Root, "extracted");
        var service = new SharpCompressArchiveService();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ExtractSplitZipAsync(
            [zipPath], "malicious.zip", 3, destination, new InlineProgress<PhaseProgress>(_ => { })));

        Assert.False(File.Exists(Path.Combine(workspace.Root, "outside.txt")));
    }
}
