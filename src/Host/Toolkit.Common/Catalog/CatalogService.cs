using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Toolkit.Common.Catalog;

/// <summary>Fetches and parses a module catalog from a URL (GitHub raw, http(s), or file://).</summary>
public sealed class CatalogService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Loads the catalog at <paramref name="url"/>. Supports http(s) and local file paths.</summary>
    public async Task<IReadOnlyList<CatalogEntry>> FetchAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Array.Empty<CatalogEntry>();

        string json;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            json = await Http.GetStringAsync(uri, ct);
        }
        else
        {
            // Local path or file:// — handy for testing/offline.
            var path = uri is { IsFile: true } ? uri.LocalPath : url;
            json = await File.ReadAllTextAsync(path, ct);
        }

        var doc = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions);
        return doc?.Modules ?? new List<CatalogEntry>();
    }

    /// <summary>Downloads a package (zip) to a temporary file and returns its path.</summary>
    public async Task<string> DownloadPackageAsync(string packageUrl, CancellationToken ct = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"dt-module-{Guid.NewGuid():N}.zip");

        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            await using var src = await Http.GetStreamAsync(uri, ct);
            await using var dst = File.Create(tempZip);
            await src.CopyToAsync(dst, ct);
        }
        else
        {
            var path = uri is { IsFile: true } ? uri.LocalPath : packageUrl;
            File.Copy(path, tempZip, overwrite: true);
        }

        return tempZip;
    }
}
