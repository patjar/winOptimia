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
        var texts = logs
            .Concat(completedTasks)
            .Select(x => x?.ToString() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        int errors = CountContains(texts, "erreur") + CountContains(texts, "error") + CountContains(texts, "failed");
        int warnings = CountContains(texts, "avert") + CountContains(texts, "warn") + CountContains(texts, "attention");
        int accessDenied = CountContains(texts, "access denied") + CountContains(texts, "accès refusé") + CountContains(texts, "acces refuse");
        int updateHits = CountContains(texts, "update") + CountContains(texts, "mise à jour") + CountContains(texts, "mise a jour");
        int diskHits = CountContains(texts, "volume") + CountContains(texts, "disque") + CountContains(texts, "storage");
        int sfcHits = CountContains(texts, "sfc");

        int safeBase = baseScore <= 0 ? 90 : Clamp(baseScore);

        int stability = Clamp(safeBase - errors * 12 - warnings * 5);
        int security = Clamp(92 - accessDenied * 10 + Math.Min(sfcHits, 2) * 2);
        int windowsUpdate = Clamp(88 - Math.Max(updateHits - 1, 0) * 4);
        int storage = Clamp(90 + Math.Min(diskHits, 3) * 2 - accessDenied * 4);
        int performance = Clamp(90 + Math.Min(workerCount, 6) - warnings * 3 - errors * 5);
        int global = Clamp((performance + security + storage + windowsUpdate + stability) / 5);

        return new HealthScore
        {
            Global = global,
            Performance = performance,
            Security = security,
            Storage = storage,
            WindowsUpdate = windowsUpdate,
            Stability = stability,
            Summary = BuildSummary(global, windowsUpdate, storage, errors, warnings, workerCount, workerMode)
        };
    }

    public string RenderText(HealthScore score)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Santé globale : {score.Global}/100");
        sb.AppendLine($"Performance : {score.Performance}/100");
        sb.AppendLine($"Sécurité : {score.Security}/100");
        sb.AppendLine($"Stockage : {score.Storage}/100");
        sb.AppendLine($"Windows Update : {score.WindowsUpdate}/100");
        sb.AppendLine($"Stabilité : {score.Stability}/100");
        sb.AppendLine();
        sb.AppendLine("Analyse :");
        sb.AppendLine(score.Summary);
        return sb.ToString();
    }

    private static int CountContains(IEnumerable<string> texts, string pattern)
    {
        int count = 0;
        foreach (string text in texts)
        {
            if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static int Clamp(int value)
    {
        return Math.Max(0, Math.Min(100, value));
    }

    private static string BuildSummary(
        int global,
        int windowsUpdate,
        int storage,
        int errors,
        int warnings,
        int workerCount,
        string workerMode)
    {
        var points = new List<string>();

        points.Add(global >= 90
            ? "Le poste est globalement dans un état très sain."
            : global >= 75
                ? "Le poste est utilisable, avec quelques points à surveiller."
                : "Le poste présente plusieurs signaux à surveiller avant une optimisation agressive.");

        if (errors > 0)
        {
            points.Add($"Erreurs détectées dans les journaux : {errors} occurrence(s). Priorité à la stabilité.");
        }

        if (warnings > 0)
        {
            points.Add($"Avertissements détectés : {warnings} occurrence(s). L'IA recommande un suivi sur les prochains lancements.");
        }

        if (windowsUpdate < 85)
        {
            points.Add("Windows Update ressort comme une zone à surveiller.");
        }

        if (storage >= 90)
        {
            points.Add("Le stockage ne présente pas de signal défavorable dans cette exécution.");
        }

        if (workerCount > 0)
        {
            points.Add($"Mode workers observé : {workerCount} worker(s), mode {workerMode}.");
        }

        return string.Join(Environment.NewLine, points);
    }
}
