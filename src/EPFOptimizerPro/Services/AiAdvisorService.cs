using System.Reflection;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class AiAdvisorService
{
    public IReadOnlyList<AiAdvisorTip> Analyze(
        IEnumerable<object>? logs,
        IEnumerable<object>? completedTasks,
        int score,
        int workerCount,
        string? workerMode,
        double cpuInitial,
        double ramInitial)
    {
        List<string> logLines = NormalizeLogs(logs).ToList();
        List<TaskSnapshot> tasks = NormalizeTasks(completedTasks).ToList();
        List<AiAdvisorTip> tips = new();

        int errorCount = CountMatches(logLines, "ERROR", "Erreur", "Exception", "failed", "échoué");
        int warningCount = CountMatches(logLines, "WARN", "Avertissement", "Access denied", "Accès refusé", "refusé");
        int updateCount = CountMatches(logLines, "Windows Update", "Updates", "Mise à jour");
        int sfcCount = CountMatches(logLines, "SFC", "scannow");
        int diskCount = CountMatches(logLines, "Volumes", "Optimize-Volume", "Disque", "Stockage");
        int completedCount = tasks.Count(t => IsStatus(t.Status, "Terminé", "OK", "Done", "Completed"));
        int taskWarningCount = tasks.Count(t => IsStatus(t.Status, "Avertissement", "WARN", "Warning"));
        int taskErrorCount = tasks.Count(t => IsStatus(t.Status, "Erreur", "ERROR", "Failed", "Échec"));

        tips.Add(BuildGlobalTip(score, errorCount, warningCount, completedCount, tasks.Count));
        tips.Add(BuildWorkerTip(workerCount, workerMode, cpuInitial, ramInitial, errorCount, warningCount));

        if (updateCount > 0)
        {
            tips.Add(new AiAdvisorTip
            {
                Severity = "INFO",
                Category = "Windows Update",
                Title = "Surveillance des mises à jour",
                Detail = "Des traces liées à Windows Update sont présentes dans le journal. L'IA locale recommande de conserver cette tâche dans le suivi, car elle influence directement le score de conformité du poste.",
                Confidence = 78
            });
        }

        if (sfcCount > 0)
        {
            tips.Add(new AiAdvisorTip
            {
                Severity = "OK",
                Category = "Stabilité",
                Title = "Contrôle d'intégrité détecté",
                Detail = "Le journal contient une exécution SFC. Ce contrôle est utile pour détecter ou réparer des fichiers système endommagés. L'IA locale recommande de le conserver dans les optimisations longues.",
                Confidence = 82
            });
        }

        if (diskCount > 0)
        {
            tips.Add(new AiAdvisorTip
            {
                Severity = warningCount > 0 ? "WARN" : "INFO",
                Category = "Stockage",
                Title = "Analyse stockage active",
                Detail = warningCount > 0
                    ? "Une activité liée au stockage est présente avec au moins un avertissement dans les journaux. Si l'optimisation volume est refusée, lance l'application avec les droits administrateur."
                    : "Une activité liée au stockage est présente et ne montre pas d'erreur critique dans les éléments analysés.",
                Confidence = warningCount > 0 ? 86 : 74
            });
        }

        if (taskWarningCount > 0 || taskErrorCount > 0)
        {
            tips.Add(new AiAdvisorTip
            {
                Severity = taskErrorCount > 0 ? "ERROR" : "WARN",
                Category = "Tâches",
                Title = "Tâches à surveiller",
                Detail = $"L'analyse des tâches terminées détecte {taskWarningCount} avertissement(s) et {taskErrorCount} erreur(s). L'IA locale recommande de consulter les cartes de tâches et le journal avant de relancer une optimisation complète.",
                Confidence = 88
            });
        }

        if (tips.Count < 3)
        {
            tips.Add(new AiAdvisorTip
            {
                Severity = "OK",
                Category = "Synthèse",
                Title = "Aucun signal critique dominant",
                Detail = "Les données transmises au moteur IA ne montrent pas de concentration forte d'erreurs ou d'avertissements. Le poste semble exploitable avec le mode actuel.",
                Confidence = 72
            });
        }

        return tips;
    }

    public string RenderText(IEnumerable<AiAdvisorTip> tips)
    {
        return string.Join(Environment.NewLine + Environment.NewLine, tips.Select(t => t.ToString()));
    }

    private static AiAdvisorTip BuildGlobalTip(int score, int errorCount, int warningCount, int completedCount, int taskCount)
    {
        if (errorCount > 0)
        {
            return new AiAdvisorTip
            {
                Severity = "ERROR",
                Category = "Santé globale",
                Title = "Erreurs détectées dans le journal",
                Detail = $"Le score courant est {score}/100, mais {errorCount} signalement(s) d'erreur ont été détectés dans les journaux. L'IA locale recommande de traiter ces erreurs avant d'augmenter le niveau d'automatisation.",
                Confidence = 92
            };
        }

        if (warningCount > 0)
        {
            return new AiAdvisorTip
            {
                Severity = "WARN",
                Category = "Santé globale",
                Title = "Avertissements présents",
                Detail = $"Le score courant est {score}/100. {warningCount} avertissement(s) sont présents dans les journaux. L'IA locale recommande de vérifier les droits administrateur, Windows Update et les étapes disque.",
                Confidence = 86
            };
        }

        return new AiAdvisorTip
        {
            Severity = score >= 85 ? "OK" : "INFO",
            Category = "Santé globale",
            Title = score >= 85 ? "Poste stable" : "Poste à surveiller",
            Detail = $"Le score courant est {score}/100. {completedCount}/{Math.Max(taskCount, completedCount)} tâche(s) sont terminées dans les données analysées. Aucun signal d'erreur dominant n'a été trouvé.",
            Confidence = score >= 85 ? 84 : 70
        };
    }

    private static AiAdvisorTip BuildWorkerTip(int workerCount, string? workerMode, double cpuInitial, double ramInitial, int errorCount, int warningCount)
    {
        string mode = string.IsNullOrWhiteSpace(workerMode) ? "mode non précisé" : workerMode;

        if (cpuInitial >= 80 || ramInitial >= 85)
        {
            return new AiAdvisorTip
            {
                Severity = "WARN",
                Category = "Workers",
                Title = "Réduire la charge parallèle",
                Detail = $"Au lancement, CPU {cpuInitial:0}% et RAM {ramInitial:0}%. Avec {workerCount} worker(s), l'IA locale recommande un mode protection pour éviter de ralentir le poste.",
                Confidence = 90
            };
        }

        if (errorCount == 0 && warningCount == 0 && cpuInitial < 45 && ramInitial < 70)
        {
            return new AiAdvisorTip
            {
                Severity = "OK",
                Category = "Workers",
                Title = "Mode workers cohérent",
                Detail = $"Le dernier contexte indique CPU {cpuInitial:0}% et RAM {ramInitial:0}%. Avec {workerCount} worker(s) en {mode}, l'IA locale considère le réglage cohérent.",
                Confidence = 83
            };
        }

        return new AiAdvisorTip
        {
            Severity = "INFO",
            Category = "Workers",
            Title = "Surveillance du parallélisme",
            Detail = $"Configuration observée : {workerCount} worker(s), {mode}, CPU initial {cpuInitial:0}%, RAM initiale {ramInitial:0}%. L'IA locale continuera à surveiller ce réglage.",
            Confidence = 74
        };
    }

    private static IEnumerable<string> NormalizeLogs(IEnumerable<object>? logs)
    {
        if (logs is null) yield break;

        foreach (object item in logs)
        {
            string? value = item?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<TaskSnapshot> NormalizeTasks(IEnumerable<object>? tasks)
    {
        if (tasks is null) yield break;

        foreach (object item in tasks)
        {
            if (item is null) continue;
            yield return new TaskSnapshot(
                ReadString(item, "Name"),
                ReadString(item, "Status"),
                ReadString(item, "Message"),
                ReadInt(item, "Progress"));
        }
    }

    private static string ReadString(object source, string propertyName)
    {
        PropertyInfo? property = source.GetType().GetProperty(propertyName);
        object? value = property?.GetValue(source);
        return value?.ToString() ?? string.Empty;
    }

    private static int ReadInt(object source, string propertyName)
    {
        PropertyInfo? property = source.GetType().GetProperty(propertyName);
        object? value = property?.GetValue(source);
        return value switch
        {
            int i => i,
            double d => (int)d,
            float f => (int)f,
            _ => int.TryParse(value?.ToString(), out int parsed) ? parsed : 0
        };
    }

    private static int CountMatches(IEnumerable<string> lines, params string[] patterns)
    {
        return lines.Count(line => patterns.Any(pattern => line.Contains(pattern, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsStatus(string status, params string[] expected)
    {
        return expected.Any(value => status.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TaskSnapshot(string Name, string Status, string Message, int Progress);
}
