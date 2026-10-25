using System.Net;
using System.Text;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class ManifestServiceTests
{
    [Fact]
    public void AcceptsValidManifestFromExactTrustedHttpsHost()
    {
        var service = CreateService();
        service.Validate(CreateManifest());

        Assert.True(service.IsTrustedHttpsUrl(new Uri("https://cdn.example.test/client.z01")));
        Assert.False(service.IsTrustedHttpsUrl(new Uri("http://cdn.example.test/client.z01")));
        Assert.False(service.IsTrustedHttpsUrl(new Uri("https://evil.example.test/client.z01")));
    }

    [Theory]
    [InlineData("../Game.exe")]
    [InlineData("C:\\Game.exe")]
    [InlineData("\\\\server\\share\\Game.exe")]
    public void RejectsUnsafeExecutablePaths(string path)
    {
        var service = CreateService();
        Assert.Throws<LauncherConfigurationException>(() => service.Validate(CreateManifest() with { EntryExecutable = path }));
    }

    [Fact]
    public void RejectsUntrustedVolumeHostsAndInvalidHashes()
    {
        var service = CreateService();
        var manifest = CreateManifest();
        Assert.Throws<LauncherConfigurationException>(() => service.Validate(manifest with
        {
            Archive = manifest.Archive with
            {
                Parts = [manifest.Archive.Parts[0] with { Url = new Uri("https://other.example.test/client.zip") }]
            }
        }));
        Assert.Throws<LauncherConfigurationException>(() => service.Validate(manifest with
        {
            Archive = manifest.Archive with
            {
                Parts = [manifest.Archive.Parts[0] with { Sha256 = "bad" }]
            }
        }));
    }

    [Fact]
    public async Task LoadsManifestOverConfiguredHttpsUrl()
    {
        var json = """
        {
          "manifestVersion": 1,
          "game": "TLBB Phong Vân Kiếm",
          "clientVersion": "1.0.0",
          "entryExecutable": "Game.exe",
          "registerUrl": "https://cdn.example.test/register",
          "archive": {
            "format": "split-zip",
            "entryPart": "client.zip",
            "expandedSizeBytes": 10,
            "parts": [{"name":"client.zip","url":"https://cdn.example.test/client.zip","size":10,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}]
          }
        }
        """;
        using var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
        var options = new LauncherOptions
        {
            ManifestUrl = "https://cdn.example.test/manifest.json",
            TrustedHosts = ["cdn.example.test"]
        };
        var service = new ManifestService(client, options);

        var manifest = await service.LoadAsync();

        Assert.Equal("1.0.0", manifest.ClientVersion);
        Assert.Equal("client.zip", manifest.Archive.EntryPart);
    }

    private static ManifestService CreateService()
    {
        var client = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        return new ManifestService(client, new LauncherOptions { TrustedHosts = ["cdn.example.test"] });
    }

    internal static LauncherManifest CreateManifest() => new()
    {
        ManifestVersion = 1,
        Game = "TLBB Phong Vân Kiếm",
        ClientVersion = "1.0.0",
        EntryExecutable = "Game.exe",
        RegisterUrl = new Uri("https://cdn.example.test/register"),
        Archive = new ArchiveDescriptor
        {
            Format = "split-zip",
            EntryPart = "client.zip",
            ExpandedSizeBytes = 10,
            Parts = [new ArchivePart
            {
                Name = "client.zip",
                Url = new Uri("https://cdn.example.test/client.zip"),
                Size = 10,
                Sha256 = new string('a', 64)
            }]
        }
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
