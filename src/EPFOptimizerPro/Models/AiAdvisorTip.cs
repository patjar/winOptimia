namespace EPFOptimizerPro.Models;

public sealed class AiAdvisorTip
{
    public string Severity { get; init; } = "INFO";
    public string Category { get; init; } = "Général";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int Confidence { get; init; } = 70;
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public override string ToString()
    {
        return $"[{Severity}] {Category} - {Title}\n{Detail}\nConfiance IA locale : {Confidence}%";
    }
}
