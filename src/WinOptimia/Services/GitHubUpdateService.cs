using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using WinOptimia.Models;

namespace WinOptimia.Services;

public sealed class GitHubUpdateService
{
    private const string Owner = "patjar";
    private const string Repository = "winOptimia";
    private readonly HttpClient _client = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GitHubUpdateService()
    {
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("winOptimia/4.0");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public string CurrentVersion
    {
        get
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
            return version;
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(string channel, CancellationToken token)
    {
        GitHubRelease? release = channel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? await GetLatestBetaReleaseAsync(token)
            : await GetLatestStableReleaseAsync(token);

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
        string updateFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "winOptimia", "Updates");
        Directory.CreateDirectory(updateFolder);
        string safeName = string.IsNullOrWhiteSpace(asset.Name) ? "winOptimia-update.zip" : asset.Name;
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

    public bool TryLaunchUpdater(string zipPath)
    {
        string baseDir = AppContext.BaseDirectory;
        string updaterPath = Path.Combine(baseDir, "WinOptimia.Updater.exe");
        if (!File.Exists(updaterPath))
        {
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(zipPath) ?? baseDir) { UseShellExecute = true });
            return false;
        }

        int processId = Environment.ProcessId;
        string applicationExe = Path.GetFileName(Environment.ProcessPath ?? "WinOptimia.exe");
        string args = $"--wait {processId} --zip "{zipPath}" --target "{baseDir}" --exe "{applicationExe}"";
        Process.Start(new ProcessStartInfo(updaterPath, args) { UseShellExecute = true });
        return true;
    }

    public void OpenReleasePage(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private async Task<GitHubRelease?> GetLatestStableReleaseAsync(CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest";
        string json = await _client.GetStringAsync(url, token);
        return JsonSerializer.Deserialize<GitHubRelease>(json, _jsonOptions);
    }

    private async Task<GitHubRelease?> GetLatestBetaReleaseAsync(CancellationToken token)
    {
        string url = $"https://api.github.com/repos/{Owner}/{Repository}/releases";
        string json = await _client.GetStringAsync(url, token);
        List<GitHubRelease>? releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, _jsonOptions);
        return releases?.FirstOrDefault(r => r.Prerelease && !r.Draft) ?? releases?.FirstOrDefault(r => !r.Draft);
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
