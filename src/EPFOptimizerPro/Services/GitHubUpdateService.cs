using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class GitHubUpdateService
{
    private const string Owner = "patjar";
    private const string Repository = "winOptimia";
    private static readonly Regex VersionRegex = new(@"(?<version>\d+\.\d+\.\d+(?:\.\d+)?)", RegexOptions.Compiled);

    private readonly HttpClient _client = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public GitHubUpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("EPFOptimizerPro/3.9.33");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

    public Task<UpdateCheckResult> CheckLatestAsync(CancellationToken token)
    {
        return CheckAsync("stable", token);
    }

    public Task<UpdateCheckResult> CheckLatestAsync(string channel, CancellationToken token)
    {
        return CheckAsync(channel, token);
    }

    public async Task<UpdateCheckResult> CheckAsync(string channel, CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=100";
        string json = await _client.GetStringAsync(url, token);
        List<GitHubRelease>? releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, _jsonOptions);

        if (releases is null || releases.Count == 0)
        {
            return NoRelease("No GitHub release found.");
        }

        Version currentVersion = ParseVersionOrZero(CurrentVersion);

        List<EpfReleaseCandidate> candidates = releases
            .Where(r => !r.Draft)
            .Where(r => channel.Equals("beta", StringComparison.OrdinalIgnoreCase) || !r.Prerelease)
            .Select(r =>
            {
                GitHubAsset? asset = FindBestEpfAsset(r);
                string? versionText = asset is null ? null : ExtractEpfVersion(r, asset);
                Version version = ParseVersionOrZero(versionText ?? string.Empty);
                return new EpfReleaseCandidate(r, asset, versionText ?? string.Empty, version);
            })
            .Where(c => c.Asset is not null)
            .Where(c => c.Version > new Version(0, 0, 0, 0))
            .OrderByDescending(c => c.Version)
            .ToList();

        EpfReleaseCandidate? latestEpf = candidates.FirstOrDefault();
        if (latestEpf is null)
        {
            return NoRelease("No usable EPFOptimizerPro release found. WinOptimia releases are ignored.");
        }

        EpfReleaseCandidate? latestNewer = candidates
            .Where(c => c.Version > currentVersion)
            .OrderByDescending(c => c.Version)
            .FirstOrDefault();

        if (latestNewer is null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = latestEpf.VersionText,
                UpdateAvailable = false,
                ReleaseUrl = latestEpf.Release.HtmlUrl,
                Notes = "No newer EPFOptimizerPro release available. WinOptimia releases are ignored.",
                Asset = null
            };
        }

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latestNewer.VersionText,
            UpdateAvailable = true,
            ReleaseUrl = latestNewer.Release.HtmlUrl,
            Notes = latestNewer.Release.Body,
            Asset = latestNewer.Asset
        };
    }

    public async Task<string> DownloadAsync(GitHubAsset asset, IProgress<double>? progress, CancellationToken token)
    {
        string updatesFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "WinOptimia",
            "Updates");

        Directory.CreateDirectory(updatesFolder);

        string safeName = string.IsNullOrWhiteSpace(asset.Name) ? "EPFOptimizerPro-update.msi" : asset.Name;
        string outputPath = Path.Combine(updatesFolder, safeName);

        if (File.Exists(outputPath))
        {
            string name = Path.GetFileNameWithoutExtension(safeName);
            string ext = Path.GetExtension(safeName);
            outputPath = Path.Combine(updatesFolder, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}{ext}");
        }

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
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), token);
            totalRead += read;

            if (totalLength.HasValue && totalLength.Value > 0)
            {
                progress?.Report(totalRead * 100.0 / totalLength.Value);
            }
        }

        progress?.Report(100);
        return outputPath;
    }

    public void OpenReleasePage(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    private UpdateCheckResult NoRelease(string notes)
    {
        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = "inconnue",
            UpdateAvailable = false,
            Notes = notes,
            Asset = null
        };
    }

    private static GitHubAsset? FindBestEpfAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        GitHubAsset? msi = release.Assets
            .Where(a => IsEpfAssetName(a.Name))
            .Where(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (msi is not null)
        {
            return msi;
        }

        return release.Assets
            .Where(a => IsEpfAssetName(a.Name))
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    private static bool IsEpfAssetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name.Contains("WinOptimia", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return name.Contains("EPFOptimizerPro", StringComparison.OrdinalIgnoreCase)
            || name.Contains("EPFOptimizer", StringComparison.OrdinalIgnoreCase)
            || name.Contains("EPF-Optimizer", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractEpfVersion(GitHubRelease release, GitHubAsset asset)
    {
        string assetName = asset.Name ?? string.Empty;
        Match assetMatch = VersionRegex.Match(assetName);
        if (assetMatch.Success)
        {
            return assetMatch.Groups["version"].Value;
        }

        string tag = release.TagName ?? string.Empty;
        if (!tag.StartsWith("epf-", StringComparison.OrdinalIgnoreCase) && !tag.Contains("EPF", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Match tagMatch = VersionRegex.Match(tag);
        return tagMatch.Success ? tagMatch.Groups["version"].Value : null;
    }

    private static Version ParseVersionOrZero(string value)
    {
        return Version.TryParse(NormalizeVersion(value), out Version? version)
            ? version
            : new Version(0, 0, 0, 0);
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        string clean = value.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[1..];
        }

        Match match = VersionRegex.Match(clean);
        return match.Success ? match.Groups["version"].Value : "0.0.0";
    }

    private sealed record EpfReleaseCandidate(
        GitHubRelease Release,
        GitHubAsset? Asset,
        string VersionText,
        Version Version);
}