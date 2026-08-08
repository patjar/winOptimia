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
    private readonly HttpClient _client = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GitHubUpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("EPFOptimizerPro/3.9.35");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion => NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0");

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
        List<GitHubRelease> releases = await GetReleasesAsync(token);
        Version currentVersion = ParseVersionOrZero(CurrentVersion);

        List<EpfReleaseCandidate> epfCandidates = releases
            .Where(r => !r.Draft)
            .Where(r => channel.Equals("beta", StringComparison.OrdinalIgnoreCase) || !r.Prerelease)
            .Select(r =>
            {
                GitHubAsset? asset = FindBestEpfInstallerAsset(r);
                string versionText = ExtractEpfVersion(r, asset);
                return new EpfReleaseCandidate(r, asset, versionText, ParseVersionOrZero(versionText));
            })
            .Where(c => c.Asset is not null)
            .Where(c => c.Version > new Version(0, 0, 0, 0))
            .OrderByDescending(c => c.Version)
            .ToList();

        EpfReleaseCandidate? latestEpf = epfCandidates.FirstOrDefault();
        if (latestEpf is null)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = "inconnue",
                UpdateAvailable = false,
                Notes = "Aucune release EPFOptimizerPro exploitable trouvee. Les releases WinOptimia sont ignorees.",
                Asset = null
            };
        }

        EpfReleaseCandidate? latestNewer = epfCandidates
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
                Notes = "Aucune release EPFOptimizerPro plus recente disponible. Les releases WinOptimia sont ignorees.",
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
        string updateFolder = GetWritableUpdateFolder();
        Directory.CreateDirectory(updateFolder);

        string safeName = string.IsNullOrWhiteSpace(asset.Name) ? "EPFOptimizerPro-update.msi" : asset.Name;
        string outputPath = GetUniquePath(Path.Combine(updateFolder, safeName));

        using HttpResponseMessage response = await _client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        long? totalLength = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(token);
        await using FileStream target = new(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        byte[] buffer = new byte[81920];
        long totalRead = 0;
        while (true)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token);
            if (read == 0) break;

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

    private async Task<List<GitHubRelease>> GetReleasesAsync(CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=100";
        string json = await _client.GetStringAsync(url, token);
        return JsonSerializer.Deserialize<List<GitHubRelease>>(json, _jsonOptions) ?? new List<GitHubRelease>();
    }

    private static GitHubAsset? FindBestEpfInstallerAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        return release.Assets
            .Where(a => IsEpfAssetName(a.Name))
            .Where(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) || a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(a => a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name)
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

    private static string ExtractEpfVersion(GitHubRelease release, GitHubAsset? asset)
    {
        string tag = release.TagName ?? string.Empty;
        string assetName = asset?.Name ?? string.Empty;

        // Priorite a l'asset EPF, car certains tags peuvent etre generiques ou non EPF.
        string version = ExtractVersion(assetName);
        if (version != "0.0.0")
        {
            return version;
        }

        // On accepte le tag seulement s'il est clairement EPF.
        if (tag.StartsWith("epf-", StringComparison.OrdinalIgnoreCase) || tag.Contains("EPF", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractVersion(tag);
        }

        return "0.0.0";
    }

    private static string ExtractVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        Match match = Regex.Match(value, @"\d+(?:\.\d+){1,3}");
        return match.Success ? NormalizeVersion(match.Value) : "0.0.0";
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

        Match match = Regex.Match(clean, @"\d+(?:\.\d+){1,3}");
        return match.Success ? match.Value : "0.0.0";
    }

    private static Version ParseVersionOrZero(string value)
    {
        string normalized = NormalizeVersion(value);
        return Version.TryParse(normalized, out Version? version) ? version : new Version(0, 0, 0, 0);
    }

    private static string GetWritableUpdateFolder()
    {
        string programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinOptimia", "Updates");
        if (CanWriteToFolder(programData))
        {
            return programData;
        }

        string local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinOptimia", "Updates");
        Directory.CreateDirectory(local);
        return local;
    }

    private static bool CanWriteToFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            string testFile = Path.Combine(folder, ".write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(folder, $"{name}-{i}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    private sealed record EpfReleaseCandidate(
        GitHubRelease Release,
        GitHubAsset? Asset,
        string VersionText,
        Version Version);
}
