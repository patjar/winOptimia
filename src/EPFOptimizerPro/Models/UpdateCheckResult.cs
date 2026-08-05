namespace EPFOptimizerPro.Models;

public sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public GitHubAsset? Asset { get; init; }
}
