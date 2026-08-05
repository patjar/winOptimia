using System.Reflection;
using System.Text;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class HealthScoreService
{
    public HealthScore Compute(
        IEnumerable<object> logs,
        IEnumerable<object> completedTasks,
        int baseScore,
        int workerCount,
        string workerMode)
    {
        var logTexts = logs.Select(x => x?.ToString() ?? string.Empty).ToList();
        var taskTexts = completedTasks.Select(x => x?.ToString() ?? string.Empty).ToList();
        var allTexts = logTexts.Concat(taskTexts).ToList();

        int errors = CountContains(allTexts, "erreur") + CountContains(allTexts, "error") + CountContains(allTexts, "failed");
        int warnings = CountContains(allTexts, "avert") + CountContains(allTexts, "warn") + CountContains(allTexts, "attention");
        int accessDenied = CountContains(allTexts, "access denied") + CountContains(allTexts, "acces refuse") + CountContains(allTexts, "accès refusé");
        int updateHits = CountContains(allTexts, "update") + CountContains(allTexts, "mise a jour") + CountContains(allTexts, "mise à jour");
        int diskHits = CountContains(allTexts, "volume") + CountContains(allTexts, "disque") + CountContains(allTexts, "storage");
        int sfcHits = CountContains(allTexts, "sfc");

        int safeBase = baseScore <= 0 ? 90 : Clamp(baseScore);

        int stability = Clamp(safeBase - errors * 12 - warnings * 5);
        int security = Clamp(92 - accessDenied * 10 + Math.Min(sfcHits, 2) * 2);
        int windowsUpdate = Clamp(88 - Math.Max(updateHits - 1, 0) * 4);
        int storage = Clamp(90 + Math.Min(diskHits, 3) * 2 - accessDenied * 4);
        int performance = Clamp(90 + Math.Min(workerCount, 6) - warnings * 3 - errors * 5);

        int global = Clamp((performance + security + storage + windowsUpdate + stability) / 5);

        string summary = BuildSummary(global, performance, security, storage, windowsUpdate, stability, errors, warnings, workerCount, workerMode);

        return new HealthScore
        {
            Global = global,
            Performance = performance,
            Security = security,
            Storage = storage,
            WindowsUpdate = windowsUpdate,
            Stability = stability,
            Summary = summary
        };
    }

    public string RenderText(HealthScore score)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sante globale : {score.Global}/100");
        sb.AppendLine($"Performance : {score.Performance}/100");
        sb.AppendLine($"Securite : {score.Security}/100");
        sb.AppendLine($"Stockage : {score.Storage}/100");
        sb.AppendLine($"Windows Update : {score.WindowsUpdate}/100");
        sb.AppendLine($"Stabilite : {score.Stability}/100");
        sb.AppendLine();
        sb.AppendLine("Analyse :");
        sb.AppendLine(score.Summary);
        return sb.ToString();
    }

    private static int CountContains(IEnumerable<string> texts, string pattern)
    {
        return texts.Count(x => x.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static int Clamp(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }

    private static string BuildSummary(
        int global,
        int performance,
        int security,
        int storage,
        int windowsUpdate,
        int stability,
        int errors,
        int warnings,
        int workerCount,
        string workerMode)
    {
        var points = new List<string>();

        if (global >= 90)
        {
            points.Add("Le poste est globalement dans un etat tres sain.");
        }
        else if (global >= 75)
        {
            points.Add("Le poste est utilisable mais certaines categories meritent une surveillance.");
        }
        else
        {
            points.Add("Le poste presente plusieurs signaux a surveiller avant une optimisation agressive.");
        }

        if (errors > 0)
        {
            points.Add($"Des erreurs ont ete detectees dans les journaux : {errors} occurrence(s).");
        }

        if (warnings > 0)
        {
            points.Add($"Des avertissements ont ete detectes : {warnings} occurrence(s). L'IA recommande de les suivre sur les prochains lancements.");
        }

        if (windowsUpdate < 85)
        {
            points.Add("Windows Update ressort comme une zone a surveiller.");
        }

        if (storage >= 90)
        {
            points.Add("Le stockage ne presente pas de signal defavorable dans cette execution.");
        }

        if (workerCount > 0)
        {
            points.Add($"Mode workers observe : {workerCount} worker(s), mode {workerMode}.");
        }

        return string.Join(Environment.NewLine, points);
    }
}
