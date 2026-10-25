using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using TLBB.Launcher.Models;

namespace TLBB.Launcher.Services;

public sealed class ManifestService(HttpClient httpClient, LauncherOptions options) : IManifestService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<LauncherManifest> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(options.ManifestUrl, UriKind.Absolute, out var manifestUri) || !IsTrustedHttpsUrl(manifestUri))
        {
            throw new LauncherConfigurationException("Nguồn manifest chưa được cấu hình bằng URL HTTPS thuộc host tin cậy.");
        }

        using var response = await httpClient.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } finalUri && !IsTrustedHttpsUrl(finalUri))
        {
            throw new LauncherConfigurationException("Manifest chuyển hướng tới host không được tin cậy.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<LauncherManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new LauncherConfigurationException("Manifest rỗng hoặc không hợp lệ.");

        Validate(manifest);
        return manifest;
    }

    public bool IsTrustedHttpsUrl(Uri? uri) =>
        uri is { IsAbsoluteUri: true, Scheme: "https" } &&
        options.TrustedHosts.Any(host => string.Equals(host.Trim(), uri.Host, StringComparison.OrdinalIgnoreCase));

    public void Validate(LauncherManifest manifest)
    {
        if (manifest.ManifestVersion != 1 || string.IsNullOrWhiteSpace(manifest.Game) || string.IsNullOrWhiteSpace(manifest.ClientVersion))
        {
            throw new LauncherConfigurationException("Manifest phải có manifestVersion=1, tên game và clientVersion.");
        }

        if (!IsSafeRelativePath(manifest.EntryExecutable))
        {
            throw new LauncherConfigurationException("Đường dẫn executable trong manifest không an toàn.");
        }

        if (manifest.RegisterUrl is not null && !IsTrustedHttpsUrl(manifest.RegisterUrl))
        {
            throw new LauncherConfigurationException("URL đăng ký phải là HTTPS và thuộc host tin cậy.");
        }

        if (!string.Equals(manifest.Archive.Format, "split-zip", StringComparison.OrdinalIgnoreCase) ||
            manifest.Archive.ExpandedSizeBytes <= 0 || manifest.Archive.Parts.Count == 0)
        {
            throw new LauncherConfigurationException("Manifest archive phải khai báo split-zip, expandedSizeBytes và ít nhất một volume.");
        }

        if (!IsSafeFileName(manifest.Archive.EntryPart) || !manifest.Archive.EntryPart.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new LauncherConfigurationException("entryPart phải là tên file .zip an toàn.");
        }

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in manifest.Archive.Parts)
        {
            if (!IsSafeFileName(part.Name) || !seenNames.Add(part.Name))
            {
                throw new LauncherConfigurationException("Tên volume trùng hoặc chứa đường dẫn không an toàn.");
            }

            if (part.Size <= 0 || !Regex.IsMatch(part.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant))
            {
                throw new LauncherConfigurationException($"Volume {part.Name} thiếu kích thước hoặc SHA-256 hợp lệ.");
            }

            if (!IsTrustedHttpsUrl(part.Url))
            {
                throw new LauncherConfigurationException($"Volume {part.Name} phải tải từ URL HTTPS thuộc host tin cậy.");
            }
        }

        if (!seenNames.Contains(manifest.Archive.EntryPart))
        {
            throw new LauncherConfigurationException("entryPart không có trong danh sách volumes.");
        }
    }

    public static bool IsSafeFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 128 &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\', ':', '\0']) < 0 &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal);

    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains(':') || path.Contains('\0'))
        {
            return false;
        }

        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(segment => segment is not "." and not "..");
    }
}
