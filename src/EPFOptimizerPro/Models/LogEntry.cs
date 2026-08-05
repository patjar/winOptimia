namespace EPFOptimizerPro.Models;

public sealed class LogEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public string Level { get; init; } = "INFO";
    public string Step { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public override string ToString()
    {
        return $"[{Time:HH:mm:ss}] [{Level}] {Step} - {Message}";
    }
}
