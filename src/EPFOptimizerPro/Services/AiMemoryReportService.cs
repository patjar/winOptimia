using System.IO;
using System.Text;
using System.Text.Json;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class AiMemoryReportService
{
    private readonly string _folder;
    private readonly string _historyFile;
    private readonly string _learningFile;
    private readonly string _summaryFile;

    public AiMemoryReportService()
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EPFOptimizerPro");

        Directory.CreateDirectory(_folder);

        _historyFile = Path.Combine(_folder, "ai_score_history.json");
        _learningFile = Path.Combine(_folder, "learning.json");
        _summaryFile = Path.Combine(_folder, "ai_memory_summary.txt");
    }

    public string Folder => _folder;
    public string SummaryFile => _summaryFile;
    public string HistoryFile => _historyFile;
    public string LearningFile => _learningFile;

    public string GenerateSummary()
    {
        EnsureFiles();

        var history = LoadHistory();
        var sb = new StringBuilder();

        sb.AppendLine("EPF Optimizer Pro - Centre memoire IA");
        sb.AppendLine("====================================");
        sb.AppendLine($"Genere le : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Dossier memoire : {_folder}");
        sb.AppendLine($"Historique IA : {_historyFile}");
        sb.AppendLine($"Memoire apprentissage : {_learningFile}");
        sb.AppendLine();

        sb.AppendLine("Historique des scores IA");
        sb.AppendLine("------------------------");
        sb.AppendLine($"Nombre d'instantanes : {history.Count}");

        if (history.Count == 0)
        {
            sb.AppendLine("Aucun instantane IA disponible pour le moment.");
        }
        else
        {
            var last = history.OrderBy(x => x.Date).Last();
            sb.AppendLine($"Dernier score global : {last.Global}/100");
            sb.AppendLine($"Performance : {last.Performance}/100");
            sb.AppendLine($"Securite : {last.Security}/100");
            sb.AppendLine($"Stockage : {last.Storage}/100");
            sb.AppendLine($"Windows Update : {last.WindowsUpdate}/100");
            sb.AppendLine($"Stabilite : {last.Stability}/100");
            sb.AppendLine($"Workers observes : {last.WorkerCount} ({last.WorkerMode})");
            sb.AppendLine($"Derniere mesure : {last.Date:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine(BuildTrend(history));
        }

        sb.AppendLine();
        sb.AppendLine("Fichiers disponibles");
        sb.AppendLine("--------------------");
        sb.AppendLine(File.Exists(_historyFile) ? "ai_score_history.json : present" : "ai_score_history.json : absent");
        sb.AppendLine(File.Exists(_learningFile) ? "learning.json : present" : "learning.json : absent");

        File.WriteAllText(_summaryFile, sb.ToString(), Encoding.UTF8);
        return _summaryFile;
    }

    private void EnsureFiles()
    {
        Directory.CreateDirectory(_folder);

        if (!File.Exists(_historyFile))
        {
            File.WriteAllText(_historyFile, "[]", Encoding.UTF8);
        }

        if (!File.Exists(_learningFile))
        {
            File.WriteAllText(_learningFile, "{}", Encoding.UTF8);
        }
    }

    private IReadOnlyList<AiScoreHistoryEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(_historyFile))
            {
                return Array.Empty<AiScoreHistoryEntry>();
            }

            string json = File.ReadAllText(_historyFile, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<AiScoreHistoryEntry>();
            }

            return JsonSerializer.Deserialize<List<AiScoreHistoryEntry>>(json)
                   ?? new List<AiScoreHistoryEntry>();
        }
        catch
        {
            return Array.Empty<AiScoreHistoryEntry>();
        }
    }

    private static string BuildTrend(IReadOnlyList<AiScoreHistoryEntry> history)
    {
        var ordered = history.OrderBy(x => x.Date).ToList();
        if (ordered.Count < 2)
        {
            return "Tendance : historique insuffisant pour comparer les executions.";
        }

        int window = Math.Min(5, ordered.Count);
        var lastItems = ordered.Skip(ordered.Count - window).ToList();
        int delta = lastItems[^1].Global - lastItems[0].Global;
        double average = lastItems.Average(x => x.Global);

        if (delta >= 5)
        {
            return $"Tendance : amelioration (+{delta} points, moyenne recente {average:0}/100).";
        }

        if (delta <= -5)
        {
            return $"Tendance : degradation ({delta} points, moyenne recente {average:0}/100).";
        }

        return $"Tendance : stable, moyenne recente {average:0}/100.";
    }
}
