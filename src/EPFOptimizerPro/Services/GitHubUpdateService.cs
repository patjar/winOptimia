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
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("EPFOptimizerPro/3.9.22");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is null)
            {
                return "0.0.0";
            }

            if (version.Revision > 0)
            {
                return version.ToString(4);
            }

            return version.ToString(3);
        }
    }

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

        var candidates = releases
            .Where(r => !r.Draft)
            .Select(r => new
            {
                Release = r,
                Asset = FindEpfUpdateAsset(r),
                VersionText = ExtractVersion(r)
            })
            .Where(x => x.Asset is not null && Version.TryParse(x.VersionText, out _))
            .Select(x => new
            {
                x.Release,
                Asset = x.Asset!,
                VersionText = x.VersionText,
                Version = Version.Parse(x.VersionText)
            })
            .OrderByDescending(x => x.Version)
            .ToList();

        if (candidates.Count == 0)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                LatestVersion = "inconnue",
                UpdateAvailable = false,
                Notes = "Aucune release EPFOptimizerPro exploitable trouvee. Attendu : asset .msi ou .zip contenant EPFOptimizerPro."
            };
        }

        var latest = candidates[0];
        bool available = IsNewer(latest.VersionText, CurrentVersion);

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latest.VersionText,
            UpdateAvailable = available,
            ReleaseUrl = latest.Release.HtmlUrl,
            Notes = latest.Release.Body,
            Asset = latest.Asset
        };
    }

    public async Task<string> DownloadAsync(GitHubAsset asset, IProgress<double>? progress, CancellationToken token)
    {
        string updateFolder = GetWritableUpdateFolder();
        Directory.CreateDirectory(updateFolder);

        string safeName = string.IsNullOrWhiteSpace(asset.Name) ? "EPFOptimizerPro-update.msi" : asset.Name;
        string outputPath = GetUniqueFilePath(Path.Combine(updateFolder, safeName));

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

    private async Task<List<GitHubRelease>> GetReleasesAsync(CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases?per_page=100";
        string json = await _client.GetStringAsync(url, token);
        return JsonSerializer.Deserialize<List<GitHubRelease>>(json, _jsonOptions) ?? new List<GitHubRelease>();
    }

    private static GitHubAsset? FindEpfUpdateAsset(GitHubRelease release)
    {
        if (release.Assets is null || release.Assets.Count == 0)
        {
            return null;
        }

        static bool IsEpfAsset(GitHubAsset asset)
        {
            string name = asset.Name ?? string.Empty;
            bool isPackage = name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                             name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

            bool isEpf = name.Contains("EPFOptimizerPro", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("EPF-Optimizer", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("EPFOptimizer", StringComparison.OrdinalIgnoreCase);

            return isPackage && isEpf;
        }

        return release.Assets
            .Where(IsEpfAsset)
            .OrderByDescending(a => (a.Name ?? string.Empty).EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .ThenBy(a => a.Name)
            .FirstOrDefault();
    }

    private static string ExtractVersion(GitHubRelease release)
    {
        string tag = release.TagName ?? string.Empty;
        Match tagMatch = Regex.Match(tag, @"(?<v>\d+\.\d+\.\d+(?:\.\d+)?)");
        if (tagMatch.Success)
        {
            return tagMatch.Groups["v"].Value;
        }

        if (release.Assets is not null)
        {
            foreach (var asset in release.Assets)
            {
                Match assetMatch = Regex.Match(asset.Name ?? string.Empty, @"(?<v>\d+\.\d+\.\d+(?:\.\d+)?)");
                if (assetMatch.Success)
                {
                    return assetMatch.Groups["v"].Value;
                }
            }
        }

        return "0.0.0";
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

    private static string GetWritableUpdateFolder()
    {
        string programData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinOptimia", "Updates");
        if (CanWriteToFolder(programData))
        {
            return programData;
        }

        string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinOptimia", "Updates");
        Directory.CreateDirectory(localAppData);
        return localAppData;
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

    private static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        string folder = Path.GetDirectoryName(path) ?? string.Empty;
        string name = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);

        for (int i = 1; i < 1000; i++)
        {
            string candidate = Path.Combine(folder, $"{name}-{i}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(folder, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}
