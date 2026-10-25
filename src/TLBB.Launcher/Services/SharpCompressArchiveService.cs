using SharpCompress.Archives.Zip;
using SharpCompress.Readers;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class SharpCompressArchiveService : IArchiveService
{
    private const int MaxEntryCount = 200_000;

    public async Task ExtractSplitZipAsync(
        IReadOnlyList<string> volumePaths,
        string entryPart,
        long expectedExpandedSize,
        string destinationDirectory,
        IProgress<PhaseProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (volumePaths.Count == 0 || expectedExpandedSize <= 0)
        {
            throw new InvalidDataException("Archive split ZIP thiếu volume hoặc expandedSizeBytes.");
        }

        var orderedVolumes = OrderVolumes(volumePaths, entryPart);
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        var rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        using var archive = ZipArchive.OpenArchive(orderedVolumes.Select(path => new FileInfo(path)).ToArray(), ReaderOptions.ForFilePath);
        var entries = archive.Entries.ToArray();
        if (entries.Length == 0 || entries.Length > MaxEntryCount)
        {
            throw new InvalidDataException("Archive rỗng hoặc có số lượng entry vượt giới hạn an toàn.");
        }

        long declaredExpandedBytes = 0;
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            if (entry.Size < 0 || declaredExpandedBytes > expectedExpandedSize - entry.Size)
            {
                throw new InvalidDataException("Archive khai báo tổng dung lượng giải nén vượt giới hạn manifest.");
            }
            declaredExpandedBytes += entry.Size;
        }

        if (declaredExpandedBytes != expectedExpandedSize)
        {
            throw new InvalidDataException("expandedSizeBytes trong manifest không khớp metadata archive.");
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long actualExpandedBytes = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryKey = entry.Key ?? string.Empty;
            if (!string.IsNullOrEmpty(entry.LinkTarget) ||
                (entry.Attrib.GetValueOrDefault() & (int)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Không giải nén symlink hoặc reparse point: {entryKey}");
            }

            var relativeName = entryKey.Replace('\\', '/');
            if (!IsSafeArchivePath(relativeName))
            {
                throw new InvalidDataException($"Archive chứa đường dẫn không an toàn: {entryKey}");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(root, relativeName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !seenPaths.Add(destinationPath))
            {
                throw new InvalidDataException($"Archive có đường dẫn trùng hoặc thoát khỏi staging: {entryKey}");
            }

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = entry.OpenEntryStream();
            await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var buffer = new byte[1024 * 128];
            long entryBytes = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                actualExpandedBytes += read;
                entryBytes += read;
                if (actualExpandedBytes > expectedExpandedSize)
                {
                    throw new InvalidDataException("Dữ liệu giải nén vượt expandedSizeBytes trong manifest.");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                progress.Report(new PhaseProgress(ProgressPhase.Extract,
                    (double)actualExpandedBytes / expectedExpandedSize,
                    $"Đang giải nén {entryKey}", entryKey));
            }

            await output.FlushAsync(cancellationToken);
            if (entryBytes != entry.Size)
            {
                throw new InvalidDataException($"Kích thước entry sau giải nén không khớp: {entryKey} (expected {entry.Size}, actual {entryBytes}).");
            }
        }

        if (actualExpandedBytes != expectedExpandedSize)
        {
            throw new InvalidDataException("Tổng dung lượng thực tế sau giải nén không khớp manifest.");
        }

        progress.Report(new PhaseProgress(ProgressPhase.Extract, 1, "Đã giải nén và xác minh archive"));
    }

    private static IReadOnlyList<string> OrderVolumes(IReadOnlyList<string> volumePaths, string entryPart)
    {
        var entryPath = volumePaths.FirstOrDefault(path => string.Equals(Path.GetFileName(path), entryPart, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("Không tìm thấy entry .zip trong danh sách volume.");

        var splitParts = volumePaths.Where(path => !string.Equals(path, entryPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), NaturalVolumeComparer.Instance)
            .Prepend(entryPath)
            .ToList();
        return splitParts;
    }

    private static bool IsSafeArchivePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains(':') || path.Contains('\0'))
        {
            return false;
        }
        return path.Split('/', StringSplitOptions.RemoveEmptyEntries).All(segment => segment is not "." and not "..");
    }

    private sealed class NaturalVolumeComparer : IComparer<string>
    {
        public static NaturalVolumeComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (left is null || right is null) return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
            var leftDigits = System.Text.RegularExpressions.Regex.Match(left, @"\.z(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var rightDigits = System.Text.RegularExpressions.Regex.Match(right, @"\.z(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (leftDigits.Success && rightDigits.Success && int.TryParse(leftDigits.Groups[1].Value, out var a) && int.TryParse(rightDigits.Groups[1].Value, out var b))
            {
                return a.CompareTo(b);
            }
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }
}
