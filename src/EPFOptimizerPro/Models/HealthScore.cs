namespace EPFOptimizerPro.Models;

public sealed class HealthScore
{
    public int Global { get; set; }
    public int Performance { get; set; }
    public int Security { get; set; }
    public int Storage { get; set; }
    public int WindowsUpdate { get; set; }
    public int Stability { get; set; }
    public string Summary { get; set; } = string.Empty;
}
