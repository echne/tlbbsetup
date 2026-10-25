using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class DownloadService(
    HttpClient httpClient,
    IHashVerificationService hashVerificationService,
    LauncherOptions options) : IDownloadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<IReadOnlyList<string>> DownloadAndVerifyAsync(
        LauncherManifest manifest,
        string stagingDirectory,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(stagingDirectory);
        var totalBytes = manifest.Archive.Parts.Sum(part => part.Size);
        long receivedBytes = 0;
        var partReceivedBytes = new long[manifest.Archive.Parts.Count];
        var semaphore = new SemaphoreSlim(Math.Clamp(options.MaxConcurrentDownloads, 1, 3));
        var verifiedPaths = new string[manifest.Archive.Parts.Count];
        var tasks = manifest.Archive.Parts.Select(async (part, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var finalPath = Path.Combine(stagingDirectory, part.Name);
                var partialPath = finalPath + ".part";
                var metaPath = partialPath + ".json";

                void ReportPartProgress(long bytesReceived, string message)
                {
                    var previous = Interlocked.Exchange(ref partReceivedBytes[index], bytesReceived);
                    var totalReceived = Interlocked.Add(ref receivedBytes, bytesReceived - previous);
                    progress.Report(new DownloadProgress(totalReceived, totalBytes, part.Name, message));
                }

                if (await hashVerificationService.VerifyAsync(finalPath, part.Size, part.Sha256, cancellationToken))
                {
                    ReportPartProgress(part.Size, $"Đã xác minh {part.Name}");
                    verifiedPaths[index] = finalPath;
                    return;
                }

                TryDelete(finalPath);
                var attempts = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await DownloadOneAsync(part, partialPath, metaPath,
                            () => Interlocked.Read(ref receivedBytes), ReportPartProgress, cancellationToken);

                        if (!await hashVerificationService.VerifyAsync(partialPath, part.Size, part.Sha256, cancellationToken))
                        {
                            TryDelete(partialPath);
                            TryDelete(metaPath);
                            ReportPartProgress(0, $"Đang thử lại {part.Name}");
                            throw new InvalidDataException($"SHA-256 hoặc kích thước của {part.Name} không khớp.");
                        }

                        File.Move(partialPath, finalPath, overwrite: true);
                        TryDelete(metaPath);
                        verifiedPaths[index] = finalPath;
                        return;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception) when (++attempts < 3)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempts - 1)), cancellationToken);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return verifiedPaths;
    }

    private async Task DownloadOneAsync(
        ArchivePart part,
        string partialPath,
        string metaPath,
        Func<long> totalReceived,
        Action<long, string> reportPartProgress,
        CancellationToken cancellationToken)
    {
        var offset = new FileInfo(partialPath).Exists ? new FileInfo(partialPath).Length : 0;
        if (offset > part.Size)
        {
            TryDelete(partialPath);
            TryDelete(metaPath);
            offset = 0;
        }

        ResumeMetadata? metadata = null;
        if (offset > 0 && File.Exists(metaPath))
        {
            try
            {
                await using var metaStream = File.OpenRead(metaPath);
                metadata = await JsonSerializer.DeserializeAsync<ResumeMetadata>(metaStream, JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                metadata = null;
            }
        }

        if (offset > 0 && (metadata is null || metadata.Url != part.Url.ToString() || metadata.ETag is null))
        {
            TryDelete(partialPath);
            TryDelete(metaPath);
            offset = 0;
        }
        reportPartProgress(offset, offset > 0 ? $"Tiếp tục tải {part.Name}" : $"Đang tải {part.Name}");

        using var request = new HttpRequestMessage(HttpMethod.Get, part.Url);
        if (offset > 0 && metadata?.ETag is not null)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
            request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(metadata.ETag));
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (offset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset == part.Size)
        {
            return;
        }

        response.EnsureSuccessStatusCode();
        var append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent &&
            response.Headers.ETag is not null && string.Equals(response.Headers.ETag.Tag, metadata?.ETag, StringComparison.Ordinal) &&
            response.Content.Headers.ContentRange?.From == offset;

        if (offset > 0 && !append)
        {
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                TryDelete(partialPath);
                TryDelete(metaPath);
                reportPartProgress(0, $"Đang khởi động lại {part.Name}");
                throw new InvalidDataException($"Máy chủ trả về Range không khớp cho {part.Name}; sẽ tải lại từ đầu.");
            }

            offset = 0;
            reportPartProgress(0, $"Đang tải lại {part.Name} từ đầu");
        }
        else if (offset == 0 && response.StatusCode == HttpStatusCode.PartialContent)
        {
            throw new InvalidDataException($"Máy chủ trả về nội dung một phần ngoài yêu cầu cho {part.Name}.");
        }

        var etag = response.Headers.ETag?.Tag;
        await SaveMetadataAsync(metaPath, new ResumeMetadata(part.Url.ToString(), etag), cancellationToken);

        var mode = append ? FileMode.Append : FileMode.Create;
        long writtenLength;
        await using (var destination = new FileStream(partialPath, mode, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (append)
            {
                reportPartProgress(offset, $"Đang tiếp tục {part.Name}");
            }
            await CopyResponseAsync(response, destination, part, reportPartProgress, append ? offset : 0, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            writtenLength = destination.Length;
        }

        if (writtenLength != part.Size)
        {
            throw new IOException($"Kích thước tải {part.Name} không khớp mong đợi ({writtenLength}/{part.Size} byte).");
        }
    }

    private static async Task CopyResponseAsync(
        HttpResponseMessage response,
        Stream destination,
        ArchivePart part,
        Action<long, string> reportPartProgress,
        long initialPartBytes,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[1024 * 128];
        var partBytesReceived = initialPartBytes;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            partBytesReceived += read;
            if (partBytesReceived > part.Size)
            {
                throw new InvalidDataException($"Máy chủ gửi nhiều byte hơn kích thước đã khai báo cho {part.Name}.");
            }
            reportPartProgress(partBytesReceived, $"Đang tải {part.Name}");
        }
    }

    private static async Task SaveMetadataAsync(string path, ResumeMetadata metadata, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, JsonOptions, cancellationToken);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record ResumeMetadata(string Url, string? ETag);
}
