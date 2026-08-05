using System.IO;
using System.Text.Json;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class LocalLearningEngine
{
    private readonly string _folder;
    private readonly string _file;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public LocalLearningEngine()
    {
        _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EPFOptimizerPro");
        _file = Path.Combine(_folder, "learning.json");
    }

    public string LearningFilePath => _file;

    public LearningData Load()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            if (!File.Exists(_file))
            {
                return new LearningData();
            }

            string json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<LearningData>(json) ?? new LearningData();
        }
        catch
        {
            return new LearningData();
        }
    }

    public void Learn(IReadOnlyList<LogEntry> logs, int score, int maxWorkers, double cpuStart, double memoryStart)
    {
        var data = Load();
        data.TotalRuns++;
        data.LastRunUtc = DateTime.UtcNow;

        int warnings = logs.Count(l => l.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase));
        int errors = logs.Count(l => l.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase));

        data.Runs.Add(new RunMemory
        {
            TimeUtc = DateTime.UtcNow,
            Score = score,
            MaxWorkers = maxWorkers,
            CpuStart = cpuStart,
            MemoryStart = memoryStart,
            WarningCount = warnings,
            ErrorCount = errors
        });

        if (data.Runs.Count > 100)
        {
            data.Runs = data.Runs.OrderByDescending(r => r.TimeUtc).Take(100).OrderBy(r => r.TimeUtc).ToList();
        }

        data.AverageScore = Math.Round(data.Runs.Average(r => r.Score), 1);

        foreach (var log in logs.Where(l => l.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase)))
        {
            data.WarningByTask.TryGetValue(log.Step, out int count);
            data.WarningByTask[log.Step] = count + 1;
        }

        Directory.CreateDirectory(_folder);
        File.WriteAllText(_file, JsonSerializer.Serialize(data, _jsonOptions));
    }

    public IReadOnlyList<AiRecommendation> Recommend()
    {
        var data = Load();
        var list = new List<AiRecommendation>();

        if (data.TotalRuns == 0)
        {
            list.Add(new AiRecommendation
            {
                Severity = "INFO",
                Title = "Moteur adaptatif prêt",
                Detail = "Le moteur apprend le comportement des tâches au fil des lancements."
            });
            return list;
        }

        list.Add(new AiRecommendation
        {
            Severity = data.AverageScore >= 85 ? "OK" : "WARN",
            Title = "Score moyen historique",
            Detail = $"Score moyen observé : {data.AverageScore}/100 sur {data.TotalRuns} lancement(s)."
        });

        var topWarning = data.WarningByTask.OrderByDescending(kv => kv.Value).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(topWarning.Key))
        {
            list.Add(new AiRecommendation
            {
                Severity = "INFO",
                Title = "Tâche à surveiller",
                Detail = $"La tâche '{topWarning.Key}' est celle qui a le plus souvent généré un avertissement."
            });
        }

        var last = data.Runs.LastOrDefault();
        if (last is not null)
        {
            list.Add(new AiRecommendation
            {
                Severity = "INFO",
                Title = "Workers adaptatifs",
                Detail = $"Dernier lancement : {last.MaxWorkers} worker(s), CPU initial {last.CpuStart:0} %, RAM initiale {last.MemoryStart:0} %."
            });
        }

        return list;
    }
}
