using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class GitHubUpdateService
{
    private const string Owner = "patjar";
    private const string Repository = "winOptimia";
    private readonly HttpClient _client = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GitHubUpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("EPFOptimizerPro/3.7");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckLatestAsync(CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
        string json = await _client.GetStringAsync(url, token);
        GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json, _jsonOptions);

        if (release is null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = "inconnue",
                UpdateAvailable = false,
                Notes = "Aucune release GitHub exploitable trouvée."
            };
        }

        string latest = NormalizeVersion(release.TagName);
        bool available = IsNewer(latest, CurrentVersion);
        GitHubAsset? asset = FindZipAsset(release);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latest,
            UpdateAvailable = available,
            ReleaseUrl = release.HtmlUrl,
            Notes = release.Body,
            Asset = asset
        };
    }
    public async Task<string> DownloadAsync(GitHubAsset asset, IProgress<double>? progress, CancellationToken token)
    {
        string updateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinOptimia", "Updates");
        Directory.CreateDirectory(updateFolder);
        string safeName = string.IsNullOrWhiteSpace(asset.Name) ? "WinOptimia-update.zip" : SanitizeFileName(asset.Name);
        string outputPath = Path.Combine(updateFolder, safeName);

        using HttpResponseMessage response = await _client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long? totalLength = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        await using FileStream target = File.Create(outputPath);
        byte[] buffer = new byte[81920];
        long totalRead = 0;

        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;
            if (totalLength.HasValue && totalLength.Value > 0) progress?.Report(totalRead * 100.0 / totalLength.Value);
        }
        progress?.Report(100);
        return outputPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        return fileName;
    }


    private static GitHubAsset? FindZipAsset(GitHubRelease release)
    {
        return release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && a.Name.Contains("winOptimia", StringComparison.OrdinalIgnoreCase))
            ?? release.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return "0.0.0";
        }

        string clean = tag.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[1..];
        }

        int suffixIndex = clean.IndexOf('-');
        if (suffixIndex >= 0)
        {
            clean = clean[..suffixIndex];
        }

        return clean;
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out Version? latestVersion))
        {
            return false;
        }

        if (!Version.TryParse(current, out Version? currentVersion))
        {
            return true;
        }

        return latestVersion > currentVersion;
    }
}

