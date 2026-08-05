namespace EPFOptimizerPro.Models;

public sealed class LearningData
{
    public int TotalRuns { get; set; }
    public double AverageScore { get; set; }
    public DateTime LastRunUtc { get; set; }
    public List<RunMemory> Runs { get; set; } = new();
    public Dictionary<string, int> WarningByTask { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class RunMemory
{
    public DateTime TimeUtc { get; set; }
    public int Score { get; set; }
    public int MaxWorkers { get; set; }
    public double CpuStart { get; set; }
    public double MemoryStart { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

public sealed class AiRecommendation
{
    public string Severity { get; init; } = "INFO";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
