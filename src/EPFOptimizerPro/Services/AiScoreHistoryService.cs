using System.Linq;
using System.IO;
using System.Text.Json;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class AiScoreHistoryService
{
    private const int MaxEntries = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _folder;
    private readonly string _file;

    public AiScoreHistoryService()
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EPFOptimizerPro");

        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "ai_score_history.json");
    }

    public string FilePath => _file;

    public IReadOnlyList<AiScoreHistoryEntry> Load()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return new List<AiScoreHistoryEntry>();
            }

            string json = File.ReadAllText(_file);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<AiScoreHistoryEntry>();
            }

            return JsonSerializer.Deserialize<List<AiScoreHistoryEntry>>(json) ?? new List<AiScoreHistoryEntry>();
        }
        catch
        {
            return new List<AiScoreHistoryEntry>();
        }
    }

    public void SaveSnapshot(HealthScore score, int workerCount, string workerMode)
    {
        var items = Load().ToList();

        var last = items.LastOrDefault();
        if (last is not null
            && (DateTime.Now - last.Date).TotalSeconds < 2
            && last.Global == score.Global
            && last.Performance == score.Performance
            && last.Security == score.Security
            && last.Storage == score.Storage
            && last.WindowsUpdate == score.WindowsUpdate
            && last.Stability == score.Stability)
        {
            return;
        }

        items.Add(new AiScoreHistoryEntry
        {
            Date = DateTime.Now,
            Global = score.Global,
            Performance = score.Performance,
            Security = score.Security,
            Storage = score.Storage,
            WindowsUpdate = score.WindowsUpdate,
            Stability = score.Stability,
            WorkerCount = workerCount,
            WorkerMode = workerMode ?? string.Empty
        });

        if (items.Count > MaxEntries)
        {
            items = items.Skip(items.Count - MaxEntries).ToList();
        }

        SaveAtomic(items);
    }

    public string GetTrendText()
    {
        var items = Load()
            .OrderBy(x => x.Date)
            .ToList();

        if (items.Count < 2)
        {
            return "Tendance IA : historique insuffisant pour comparer les exécutions.";
        }

        int windowSize = Math.Min(5, items.Count);
        var last = items.Skip(items.Count - windowSize).ToList();
        int firstScore = last[0].Global;
        int lastScore = last[^1].Global;
        int delta = lastScore - firstScore;
        double average = last.Average(x => x.Global);

        if (delta >= 5)
        {
            return $"Tendance IA : amélioration observée (+{delta} points sur les dernières exécutions, moyenne {average:0}/100).";
        }

        if (delta <= -5)
        {
            return $"Tendance IA : dégradation à surveiller ({delta} points sur les dernières exécutions, moyenne {average:0}/100).";
        }

        return $"Tendance IA : stable sur les dernières exécutions, moyenne {average:0}/100.";
    }

    private void SaveAtomic(List<AiScoreHistoryEntry> items)
    {
        string json = JsonSerializer.Serialize(items, JsonOptions);
        string tempFile = _file + ".tmp";

        File.WriteAllText(tempFile, json);

        if (File.Exists(_file))
        {
            File.Replace(tempFile, _file, null);
        }
        else
        {
            File.Move(tempFile, _file);
        }
    }
}


