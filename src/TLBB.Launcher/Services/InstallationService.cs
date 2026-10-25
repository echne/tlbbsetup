using System.Text.Json;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class InstallationService(
    IArchiveService archiveService,
    IHashVerificationService hashVerificationService,
    IShortcutService shortcutService,
    LauncherOptions options) : IInstallationService
{
    private const string InstalledMarkerName = ".tlbb-pvk-install.json";
    private const string TransactionMarkerName = ".tlbb-pvk-transaction.json";
    private const string DownloadStagingDirectoryName = ".staging";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public bool HasPendingCommit(string installDirectory, string clientVersion)
    {
        var target = Path.GetFullPath(installDirectory);
        var path = Path.Combine(target, TransactionMarkerName);
        if (!File.Exists(path)) return false;
        try
        {
            var transaction = JsonSerializer.Deserialize<InstallTransaction>(File.ReadAllText(path), JsonOptions);
            return transaction is not null && transaction.ClientVersion == clientVersion &&
                string.Equals(Path.GetFullPath(transaction.TargetDirectory), target, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    public async Task InstallAsync(
        LauncherManifest manifest,
        IReadOnlyList<string> verifiedParts,
        string installDirectory,
        bool createDesktopShortcut,
        IProgress<PhaseProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var target = Path.GetFullPath(installDirectory);
        var parent = Path.GetDirectoryName(target) ?? throw new InvalidOperationException("Thư mục cài đặt không hợp lệ.");
        var transactionPath = Path.Combine(target, TransactionMarkerName);
        if (Directory.Exists(target) && File.Exists(transactionPath))
        {
            var pending = await ReadTransactionAsync(transactionPath, cancellationToken);
            if (pending is null || !string.Equals(Path.GetFullPath(pending.TargetDirectory), target, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Transaction cài đặt đang chờ bị hỏng hoặc không khớp thư mục đích.");
            }
            if (pending.ClientVersion == manifest.ClientVersion)
            {
                await FinalizePendingAsync(pending, verifiedParts, progress, cancellationToken);
                return;
            }
            throw new IOException($"Phiên cài đặt {pending.ClientVersion} chưa dọn dẹp xong. Hãy hoàn tất phiên đó trước khi cài phiên bản khác.");
        }

        if (verifiedParts.Count != manifest.Archive.Parts.Count)
        {
            throw new InvalidDataException("Thiếu một hoặc nhiều volume đã xác minh.");
        }

        var downloadStagingDirectory = Path.GetDirectoryName(Path.GetFullPath(verifiedParts[0]))
            ?? throw new IOException("Không xác định được thư mục chứa các volume đã tải.");
        EnsureAllowedStagingDirectory(target, downloadStagingDirectory);

        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(parent, $".{Path.GetFileName(target)}.staging-{Guid.NewGuid():N}");
        var backupDirectory = Path.Combine(parent, $".{Path.GetFileName(target)}.backup-{Guid.NewGuid():N}");
        var committed = false;
        try
        {
            long verifiedBytes = 0;
            for (var index = 0; index < manifest.Archive.Parts.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var part = manifest.Archive.Parts[index];
                var partPath = verifiedParts[index];
                EnsureChildPath(downloadStagingDirectory, partPath);
                if (!await hashVerificationService.VerifyAsync(partPath, part.Size, part.Sha256, cancellationToken))
                {
                    throw new InvalidDataException($"Volume {part.Name} không vượt qua xác minh SHA-256 lần cuối.");
                }
                verifiedBytes += part.Size;
                progress.Report(new PhaseProgress(ProgressPhase.Verify,
                    (double)verifiedBytes / manifest.Archive.Parts.Sum(value => value.Size),
                    $"Đã xác minh {part.Name}", part.Name));
            }

            await archiveService.ExtractSplitZipAsync(
                verifiedParts,
                manifest.Archive.EntryPart,
                manifest.Archive.ExpandedSizeBytes,
                stagingDirectory,
                progress,
                cancellationToken);

            var stagedExecutable = Path.GetFullPath(Path.Combine(stagingDirectory, manifest.EntryExecutable));
            var stagePrefix = Path.EndsInDirectorySeparator(Path.GetFullPath(stagingDirectory))
                ? Path.GetFullPath(stagingDirectory)
                : Path.GetFullPath(stagingDirectory) + Path.DirectorySeparatorChar;
            if (!stagedExecutable.StartsWith(stagePrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(stagedExecutable))
            {
                throw new FileNotFoundException("Archive đã giải nén nhưng không có executable được khai báo trong manifest.", stagedExecutable);
            }

            progress.Report(new PhaseProgress(ProgressPhase.Finalize, 0.45, "Đang chuẩn bị commit an toàn"));
            if (Directory.Exists(target))
            {
                var installedMarker = await ReadTransactionAsync(Path.Combine(target, InstalledMarkerName), cancellationToken);
                var isManaged = installedMarker is not null && string.Equals(
                    Path.GetFullPath(installedMarker.TargetDirectory), target, StringComparison.OrdinalIgnoreCase);
                var existingEntries = Directory.EnumerateFileSystemEntries(target).ToArray();
                var stagingPath = Path.Combine(target, DownloadStagingDirectoryName);
                var hasUnexpectedEntries = existingEntries.Any(entry =>
                    !string.Equals(Path.GetFileName(entry), DownloadStagingDirectoryName, StringComparison.OrdinalIgnoreCase));
                if (File.Exists(stagingPath))
                {
                    throw new IOException("Đường dẫn .staging đang là một tệp. Hãy xóa hoặc đổi tên tệp đó rồi thử lại.");
                }
                if (Directory.Exists(stagingPath) &&
                    (File.GetAttributes(stagingPath) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Thư mục .staging đang trỏ tới một vị trí khác. Hãy xóa liên kết đó rồi thử lại.");
                }
                if (hasUnexpectedEntries && !isManaged)
                {
                    throw new IOException("Thư mục đích đã có dữ liệu không do launcher quản lý. Hãy chọn thư mục khác để tránh ghi đè.");
                }
                Directory.Move(target, backupDirectory);
            }

            try
            {
                Directory.Move(stagingDirectory, target);
                committed = true;
                var transaction = new InstallTransaction(manifest.ClientVersion, target, backupDirectory,
                    downloadStagingDirectory, manifest.Archive.Parts.Select(part => part.Name).ToArray(),
                    manifest.EntryExecutable, createDesktopShortcut);
                await WriteTransactionAsync(Path.Combine(target, TransactionMarkerName), transaction, cancellationToken);
            }
            catch
            {
                if (Directory.Exists(target) && Directory.Exists(backupDirectory))
                {
                    Directory.Delete(target, recursive: true);
                    Directory.Move(backupDirectory, target);
                }
                else if (!Directory.Exists(target) && Directory.Exists(backupDirectory))
                {
                    Directory.Move(backupDirectory, target);
                }
                else if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                throw;
            }

            progress.Report(new PhaseProgress(ProgressPhase.Finalize, 1, "Đã commit file game an toàn"));
            await FinalizePendingAsync(
                new InstallTransaction(manifest.ClientVersion, target, backupDirectory,
                    downloadStagingDirectory, manifest.Archive.Parts.Select(part => part.Name).ToArray(),
                    manifest.EntryExecutable, createDesktopShortcut),
                verifiedParts,
                progress,
                cancellationToken);
        }
        catch
        {
            if (!committed && Directory.Exists(stagingDirectory))
            {
                TryDeleteDirectory(stagingDirectory);
            }
            throw;
        }
    }

    private async Task FinalizePendingAsync(
        InstallTransaction transaction,
        IReadOnlyList<string> verifiedParts,
        IProgress<PhaseProgress> progress,
        CancellationToken cancellationToken)
    {
        var target = Path.GetFullPath(transaction.TargetDirectory);
        EnsureAllowedStagingDirectory(target, transaction.StagingDirectory);
        var targetParent = Path.GetDirectoryName(target) ?? throw new IOException("Transaction target không hợp lệ.");
        var backupPath = Path.GetFullPath(transaction.BackupDirectory);
        var expectedBackupPrefix = $".{Path.GetFileName(target)}.backup-";
        if (!string.Equals(Path.GetDirectoryName(backupPath), targetParent, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(backupPath).StartsWith(expectedBackupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Đường dẫn backup trong transaction không an toàn.");
        }

        var executable = Path.GetFullPath(Path.Combine(target, transaction.EntryExecutable));
        var targetPrefix = Path.EndsInDirectorySeparator(target) ? target : target + Path.DirectorySeparatorChar;
        if (!executable.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
        {
            throw new FileNotFoundException("Executable bị thiếu sau commit; game chưa được đánh dấu hoàn tất.", executable);
        }

        if (transaction.CreateDesktopShortcut)
        {
            shortcutService.CreateDesktopShortcut(target, transaction.EntryExecutable);
        }

        var cleanupItems = verifiedParts.Count > 0
            ? verifiedParts.ToArray()
            : transaction.PartNames.Select(name =>
            {
                if (!ManifestService.IsSafeFileName(name)) throw new IOException("Tên volume trong transaction không an toàn.");
                return Path.Combine(transaction.StagingDirectory, name);
            }).ToArray();
        for (var index = 0; index < cleanupItems.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(cleanupItems[index]);
            EnsureChildPath(transaction.StagingDirectory, path);
            DeleteIfExists(path);
            DeleteIfExists(path + ".part");
            DeleteIfExists(path + ".part.json");
            progress.Report(new PhaseProgress(ProgressPhase.Cleanup,
                (double)(index + 1) / Math.Max(1, cleanupItems.Length + 2),
                $"Đang dọn dữ liệu tải xuống ({index + 1}/{cleanupItems.Length})"));
        }

        var downloadStagingPath = Path.GetFullPath(Path.Combine(target, DownloadStagingDirectoryName));
        if (!string.Equals(Path.GetFullPath(transaction.StagingDirectory), downloadStagingPath, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(transaction.StagingDirectory))
        {
            Directory.Delete(transaction.StagingDirectory, recursive: true);
        }

        progress.Report(new PhaseProgress(ProgressPhase.Cleanup, 0.9, "Đang dọn bản cũ an toàn"));
        if (Directory.Exists(backupPath))
        {
            Directory.Delete(backupPath, recursive: true);
        }

        await WriteTransactionAsync(Path.Combine(target, InstalledMarkerName), transaction, cancellationToken);
        DeleteIfExists(Path.Combine(target, TransactionMarkerName));
        progress.Report(new PhaseProgress(ProgressPhase.Cleanup, 1, "Đã dọn dẹp hoàn tất"));
    }

    private static void EnsureChildPath(string parent, string path)
    {
        var root = Path.GetFullPath(parent);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Đường dẫn staging vượt khỏi thư mục ứng dụng.");
        }
    }

    private void EnsureAllowedStagingDirectory(string target, string stagingDirectory)
    {
        var actual = Path.GetFullPath(stagingDirectory);
        var expected = Path.GetFullPath(Path.Combine(target, DownloadStagingDirectoryName));
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return;

        var legacyRoot = Path.GetFullPath(options.EffectiveStagingRoot);
        var legacyPrefix = Path.EndsInDirectorySeparator(legacyRoot) ? legacyRoot : legacyRoot + Path.DirectorySeparatorChar;
        if (actual.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)) return;

        throw new IOException("Thư mục staging trong transaction không hợp lệ.");
    }

    private static async Task<InstallTransaction?> ReadTransactionAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<InstallTransaction>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteTransactionAsync(string path, InstallTransaction transaction, CancellationToken cancellationToken)
    {
        var tempPath = path + ".tmp";
        await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, transaction, JsonOptions, cancellationToken);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record InstallTransaction(
        string ClientVersion,
        string TargetDirectory,
        string BackupDirectory,
        string StagingDirectory,
        IReadOnlyList<string> PartNames,
        string EntryExecutable,
        bool CreateDesktopShortcut);
}
