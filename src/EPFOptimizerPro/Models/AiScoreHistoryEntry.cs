namespace EPFOptimizerPro.Models;

public sealed class AiScoreHistoryEntry
{
    public DateTime Date { get; set; } = DateTime.Now;
    public int Global { get; set; }
    public int Performance { get; set; }
    public int Security { get; set; }
    public int Storage { get; set; }
    public int WindowsUpdate { get; set; }
    public int Stability { get; set; }
    public int WorkerCount { get; set; }
    public string WorkerMode { get; set; } = string.Empty;
}
