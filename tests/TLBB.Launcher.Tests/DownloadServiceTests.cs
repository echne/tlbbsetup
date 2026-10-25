using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TLBB.Launcher.Models;
using TLBB.Launcher.Services;

namespace TLBB.Launcher.Tests;

public sealed class DownloadServiceTests
{
    [Fact]
    public async Task DownloadsAndVerifiesACompleteVolume()
    {
        using var workspace = new TestWorkspace("pvk-download");
        var bytes = Encoding.UTF8.GetBytes("split archive data");
        var part = CreatePart("client.z01", bytes);
        using var client = new HttpClient(new StubHandler(_ => Response(bytes, "\"v1\"")));
        var service = CreateService(client);
        var progress = new List<DownloadProgress>();

        var result = await service.DownloadAndVerifyAsync(
            CreateManifest(part),
            workspace.Root,
            new InlineProgress<DownloadProgress>(progress.Add));

        Assert.Single(result);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result[0]));
        Assert.False(File.Exists(result[0] + ".part"));
        Assert.Equal(bytes.Length, progress.Last().BytesReceived);
    }

    [Fact]
    public async Task ResumesPartialVolumeOnlyWhenServerEtagMatches()
    {
        using var workspace = new TestWorkspace("pvk-resume");
        var bytes = Encoding.UTF8.GetBytes("resumable archive");
        var part = CreatePart("client.z01", bytes);
        var partialPath = Path.Combine(workspace.Root, part.Name + ".part");
        var metaPath = partialPath + ".json";
        var splitAt = 7;
        await File.WriteAllBytesAsync(partialPath, bytes[..splitAt]);
        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(new { Url = part.Url.ToString(), ETag = "\"v1\"" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var handler = new StubHandler(request =>
        {
            Assert.Equal((long)splitAt, request.Headers.Range?.Ranges.Single().From);
            Assert.Equal("\"v1\"", request.Headers.IfRange?.EntityTag?.Tag);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent(bytes[splitAt..])
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(splitAt, bytes.Length - 1, bytes.Length);
            return response;
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.DownloadAndVerifyAsync(CreateManifest(part), workspace.Root, new InlineProgress<DownloadProgress>(_ => { }));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(result[0]));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task RestartsPartialVolumeWhenEtagChanges()
    {
        using var workspace = new TestWorkspace("pvk-etag-change");
        var bytes = Encoding.UTF8.GetBytes("download data after source change");
        var part = CreatePart("client.z01", bytes);
        var partialPath = Path.Combine(workspace.Root, part.Name + ".part");
        await File.WriteAllBytesAsync(partialPath, "old"u8.ToArray());
        await File.WriteAllTextAsync(partialPath + ".json", JsonSerializer.Serialize(new { Url = part.Url.ToString(), ETag = "\"old\"" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var handler = new StubHandler(request =>
        {
            Assert.NotNull(request.Headers.Range);
            return Response(bytes, "\"new\"");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.DownloadAndVerifyAsync(CreateManifest(part), workspace.Root, new InlineProgress<DownloadProgress>(_ => { }));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(result[0]));
    }

    [Fact]
    public async Task RetriesOnlyTheCorruptVolume()
    {
        using var workspace = new TestWorkspace("pvk-retry-corrupt");
        var bytes = Encoding.UTF8.GetBytes("valid archive data");
        var part = CreatePart("client.z01", bytes);
        var requestCount = 0;
        var progress = new List<DownloadProgress>();
        var handler = new StubHandler(_ =>
        {
            requestCount++;
            return Response(requestCount == 1 ? Encoding.UTF8.GetBytes("broken archive data") : bytes, "\"v1\"");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client);

        var result = await service.DownloadAndVerifyAsync(CreateManifest(part), workspace.Root, new InlineProgress<DownloadProgress>(progress.Add));

        Assert.Equal(2, requestCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result[0]));
        Assert.Equal(bytes.Length, progress.Max(item => item.BytesReceived));
        Assert.Equal(bytes.Length, progress.Last().BytesReceived);
    }

    [Fact]
    public async Task NeverDownloadsMoreThanThreeVolumesConcurrently()
    {
        using var workspace = new TestWorkspace("pvk-download-concurrency");
        var bytes = Encoding.UTF8.GetBytes("volume data");
        ArchivePart[] parts =
        [
            CreatePart("client.z01", bytes),
            CreatePart("client.z02", bytes),
            CreatePart("client.z03", bytes),
            CreatePart("client.zip", bytes)
        ];
        var activeRequests = 0;
        var maxConcurrentRequests = 0;
        var handler = new StubHandler(_ =>
        {
            var active = Interlocked.Increment(ref activeRequests);
            while (true)
            {
                var observed = Volatile.Read(ref maxConcurrentRequests);
                if (active <= observed || Interlocked.CompareExchange(ref maxConcurrentRequests, active, observed) == observed)
                {
                    break;
                }
            }

            Thread.Sleep(40);
            Interlocked.Decrement(ref activeRequests);
            return Response(bytes, "\"v1\"");
        });
        using var client = new HttpClient(handler);
        var service = CreateService(client, maxConcurrentDownloads: 4);

        var result = await service.DownloadAndVerifyAsync(CreateManifest(parts), workspace.Root, new InlineProgress<DownloadProgress>(_ => { }));

        Assert.Equal(4, result.Count);
        Assert.InRange(maxConcurrentRequests, 1, 3);
    }

    private static DownloadService CreateService(HttpClient client, int maxConcurrentDownloads = 3) =>
        new(client, new HashVerificationService(), new LauncherOptions { MaxConcurrentDownloads = maxConcurrentDownloads });

    private static ArchivePart CreatePart(string name, byte[] bytes) => new()
    {
        Name = name,
        Url = new Uri($"https://cdn.example.test/{name}"),
        Size = bytes.Length,
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
    };

    private static LauncherManifest CreateManifest(ArchivePart part) => CreateManifest([part]);

    private static LauncherManifest CreateManifest(IReadOnlyList<ArchivePart> parts) => new()
    {
        ManifestVersion = 1,
        Game = "test",
        ClientVersion = "1.0.0",
        EntryExecutable = "Game.exe",
        Archive = new ArchiveDescriptor
        {
            Format = "split-zip",
            EntryPart = parts.FirstOrDefault(part => part.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))?.Name ?? "client.zip",
            ExpandedSizeBytes = 10,
            Parts = parts
        }
    };

    private static HttpResponseMessage Response(byte[] body, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        response.Headers.ETag = new EntityTagHeaderValue(etag);
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private int _requestCount;
        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(responseFactory(request));
        }
    }
}
