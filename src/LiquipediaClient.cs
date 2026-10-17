using System.Net;
using System.Text.Json;

namespace OwEsportsTracker;

/// <summary>
/// Minimal, polite client for the Liquipedia Overwatch MediaWiki API.
///
/// Liquipedia's API terms ask for: a descriptive User-Agent with contact info,
/// gzip support, at most one request every 2 seconds, and aggressive caching.
/// https://liquipedia.net/api-terms-of-use
/// </summary>
public sealed class LiquipediaClient : IDisposable
{
    private const string ApiUrl = "https://liquipedia.net/overwatch/api.php";
    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2.1);

    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly TimeSpan _cacheTtl;
    private DateTime _lastRequest = DateTime.MinValue;

    public LiquipediaClient(string userAgent, string cacheDir, TimeSpan cacheTtl)
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _cacheDir = cacheDir;
        _cacheTtl = cacheTtl;
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>The page itself plus every sub-page (Regular_Season, Playoffs, ...).</summary>
    public async Task<List<string>> GetPageFamilyAsync(string page)
    {
        var titles = new List<string> { page };
        var json = await GetAsync(
            $"action=query&list=allpages&apprefix={Uri.EscapeDataString(page.Replace('_', ' ') + "/")}&aplimit=50&format=json",
            $"family_{Slug(page)}");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("allpages", out var pages))
            foreach (var p in pages.EnumerateArray())
                titles.Add(p.GetProperty("title").GetString()!.Replace(' ', '_'));
        return titles;
    }

    /// <summary>Raw wikitext for up to 50 pages in a single request.</summary>
    public async Task<Dictionary<string, string>> GetWikitextAsync(IReadOnlyList<string> titles)
    {
        var result = new Dictionary<string, string>();
        foreach (var batch in titles.Chunk(50))
        {
            var joined = string.Join('|', batch);
            var json = await GetAsync(
                $"action=query&prop=revisions&rvprop=content&rvslots=main&format=json&formatversion=2&titles={Uri.EscapeDataString(joined)}",
                $"wikitext_{Slug(joined)}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("query", out var q)) continue;
            foreach (var p in q.GetProperty("pages").EnumerateArray())
            {
                if (p.TryGetProperty("missing", out _) || !p.TryGetProperty("revisions", out var revs)) continue;
                var content = revs[0].GetProperty("slots").GetProperty("main").GetProperty("content").GetString();
                if (content is not null) result[p.GetProperty("title").GetString()!] = content;
            }
        }
        return result;
    }

    private async Task<string> GetAsync(string query, string cacheKey)
    {
        var cacheFile = Path.Combine(_cacheDir, cacheKey + ".json");
        if (File.Exists(cacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile) < _cacheTtl)
            return await File.ReadAllTextAsync(cacheFile);

        var wait = MinGap - (DateTime.UtcNow - _lastRequest);
        if (wait > TimeSpan.Zero) await Task.Delay(wait);

        Console.WriteLine($"  GET liquipedia {cacheKey}");
        using var resp = await _http.GetAsync($"{ApiUrl}?{query}");
        _lastRequest = DateTime.UtcNow;
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        await File.WriteAllTextAsync(cacheFile, body);
        return body;
    }

    private static string Slug(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray();
        var slug = new string(chars).Trim('_');
        return slug.Length > 120 ? slug[..80] + "_" + StableHash(s).ToString("x8") : slug;
    }

    // string.GetHashCode() changes between runs in .NET, so use FNV-1a for stable cache file names.
    private static uint StableHash(string s)
    {
        uint h = 2166136261;
        foreach (var c in s) { h ^= c; h *= 16777619; }
        return h;
    }

    public void Dispose() => _http.Dispose();
}
