using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Threading;
using EPFOptimizerPro.Models;

namespace EPFOptimizerPro.Services;

public sealed class AdaptiveTaskEngine
{
    private readonly Dispatcher _dispatcher;
    private readonly PowerShellCommandRunner _runner = new();
    private readonly LocalLearningEngine _learning = new();
    private readonly Dictionary<TaskProgressInfo, (string Command, int Timeout)> _commandTable = new();
    private readonly Stopwatch _uiStopwatch = Stopwatch.StartNew();
    private long _lastGlobalTick;
    private int _lastGlobalProgress = -1;

    public AdaptiveTaskEngine(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public ObservableCollection<TaskProgressInfo> Tasks { get; } = new();
    public ObservableCollection<TaskProgressInfo> ActiveTasks { get; } = new();
    public ObservableCollection<TaskProgressInfo> CompletedTasks { get; } = new();
    public List<LogEntry> Logs { get; } = new();

    public event Action<int, string>? GlobalProgressChanged;
    public event Action<LogEntry>? LogWritten;
    public event Action<IReadOnlyList<AiRecommendation>>? RecommendationsUpdated;
    public event Action<int>? ScoreUpdated;
    public event Action<int, string>? WorkerModeChanged;

    public string LearningFilePath => _learning.LearningFilePath;
    public IReadOnlyList<AiRecommendation> CurrentRecommendations => _learning.Recommend();

    public async Task<string> RunAsync(bool optimize, CancellationToken token, double cpuStart, double memoryStart)
    {
        Logs.Clear();
        InitTasks(optimize);

        int maxWorkers = CalculateWorkers(cpuStart, memoryStart);
        WorkerModeChanged?.Invoke(maxWorkers, WorkerMode(cpuStart, memoryStart));

        Log("OK", "Démarrage", optimize ? "Tâches indépendantes : audit + optimisation" : "Tâches indépendantes : audit seul");
        Log("INFO", "Moteur adaptatif", $"{maxWorkers} worker(s) sélectionné(s) selon CPU {cpuStart:0} % et RAM {memoryStart:0} %." );

        using var semaphore = new SemaphoreSlim(maxWorkers);
        var running = Tasks.Select(item => RunTaskWithSemaphoreAsync(item, semaphore, token)).ToList();
        await Task.WhenAll(running);

        int score = ComputeScore();
        _learning.Learn(Logs, score, maxWorkers, cpuStart, memoryStart);
        ScoreUpdated?.Invoke(score);

        var recommendations = _learning.Recommend();
        RecommendationsUpdated?.Invoke(recommendations);
        foreach (var recommendation in recommendations.Take(3))
        {
            Log(recommendation.Severity, "IA locale", recommendation.Title + " - " + recommendation.Detail);
        }

        RaiseGlobalProgressThrottled(96, "Rapport HTML", true);
        string report = CreateReport(recommendations, score, maxWorkers, cpuStart, memoryStart);
        Log("OK", "Rapport", report);
        RaiseGlobalProgressThrottled(100, "Terminé", true);
        return report;
    }

    private void InitTasks(bool optimize)
    {
        _dispatcher.Invoke(() =>
        {
            Tasks.Clear();
            ActiveTasks.Clear();
            CompletedTasks.Clear();
        });
        _commandTable.Clear();

        AddTask("Audit", "Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber | Format-List | Out-String", 30);
        AddTask("Updates", "$session = New-Object -ComObject Microsoft.Update.Session; $searcher = $session.CreateUpdateSearcher(); $result = $searcher.Search('IsInstalled=0 and IsHidden=0'); 'Mises à jour disponibles : ' + $result.Updates.Count", 240);

        if (optimize)
        {
            AddTask("Temp User", "Get-ChildItem -Path $env:TEMP -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue", 90);
            AddTask("Temp Win", "Get-ChildItem -Path 'C:\\Windows\\Temp' -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue", 90);
            AddTask("Corbeille", "Clear-RecycleBin -Force -ErrorAction SilentlyContinue", 60);
            AddTask("DNS", "ipconfig /flushdns", 30);
            AddTask("Volumes", "Get-Volume | Where-Object DriveLetter | ForEach-Object { Optimize-Volume -DriveLetter $_.DriveLetter }", 600);
            AddTask("SFC", "sfc /scannow", 1200);
        }
    }

    private void AddTask(string name, string command, int timeoutSeconds)
    {
        var item = new TaskProgressInfo
        {
            Name = name,
            Icon = IconForTask(name),
            Status = "En attente",
            Message = "En attente d'exécution",
            Progress = 0,
            StatusColor = "#94A3B8"
        };

        _commandTable[item] = (command, timeoutSeconds);
        _dispatcher.Invoke(() =>
        {
            Tasks.Add(item);
            ActiveTasks.Add(item);
        });
    }

    private async Task RunTaskWithSemaphoreAsync(TaskProgressInfo task, SemaphoreSlim semaphore, CancellationToken token)
    {
        await semaphore.WaitAsync(token);
        try
        {
            await RunSingleTaskAsync(task, token);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task RunSingleTaskAsync(TaskProgressInfo task, CancellationToken token)
    {
        var config = _commandTable[task];
        SetTask(task, "En cours", 3, "Démarrage", "#38BDF8");
        RaiseGlobalProgressThrottled(AverageProgress(), task.Name);

        Task<string> commandTask = _runner.RunAsync(config.Command, token, config.Timeout);
        int simulated = 3;
        int lastPublished = 0;

        while (!commandTask.IsCompleted)
        {
            token.ThrowIfCancellationRequested();
            simulated = Math.Min(90, simulated + ProgressIncrement(config.Timeout));
            if (simulated - lastPublished >= 5)
            {
                lastPublished = simulated;
                SetTask(task, "En cours", simulated, "Exécution en cours", "#38BDF8");
                RaiseGlobalProgressThrottled(AverageProgress(), task.Name);
            }
            await Task.Delay(900, token);
        }

        try
        {
            string output = await commandTask;
            string line = string.IsNullOrWhiteSpace(output) ? "Terminé" : FirstLine(output);
            if (IsWarning(line))
            {
                SetTask(task, "Avertissement", 100, FriendlyWarning(line), "#F59E0B");
                MoveToCompleted(task);
                Log("WARN", task.Name, FriendlyWarning(line));
            }
            else
            {
                SetTask(task, "Terminé", 100, line, "#22C55E");
                MoveToCompleted(task);
                Log("OK", task.Name, line);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetTask(task, "Erreur", 100, ex.Message, "#EF4444");
            MoveToCompleted(task);
            Log("ERROR", task.Name, ex.Message);
        }

        RaiseGlobalProgressThrottled(AverageProgress(), task.Name);
    }


    private void MoveToCompleted(TaskProgressInfo task)
    {
        _dispatcher.Invoke(() =>
        {
            if (ActiveTasks.Contains(task))
            {
                ActiveTasks.Remove(task);
            }

            if (!CompletedTasks.Contains(task))
            {
                CompletedTasks.Add(task);
            }
        });
    }

    private static string IconForTask(string name)
    {
        return name switch
        {
            "Audit" => "🔎",
            "Updates" => "⬆",
            "Temp User" => "🗑",
            "Temp Win" => "🧹",
            "Corbeille" => "♻",
            "DNS" => "🌐",
            "Volumes" => "💽",
            "SFC" => "🛡",
            _ => "•"
        };
    }

    private static int ProgressIncrement(int timeoutSeconds)
    {
        if (timeoutSeconds <= 60) return 18;
        if (timeoutSeconds <= 120) return 12;
        if (timeoutSeconds <= 300) return 7;
        return 4;
    }

    private void SetTask(TaskProgressInfo task, string status, int progress, string message, string color)
    {
        _dispatcher.BeginInvoke(new Action(() =>
        {
            task.Status = status;
            task.Progress = progress;
            task.Message = message;
            task.StatusColor = color;
        }), DispatcherPriority.Background);
    }


    private void RaiseGlobalProgressThrottled(int progress, string step, bool force = false)
    {
        long now = _uiStopwatch.ElapsedMilliseconds;
        if (!force && Math.Abs(progress - _lastGlobalProgress) < 2 && now - _lastGlobalTick < 300)
        {
            return;
        }

        _lastGlobalTick = now;
        _lastGlobalProgress = progress;
        GlobalProgressChanged?.Invoke(progress, step);
    }

    private int AverageProgress()
    {
        if (Tasks.Count == 0) return 0;
        return (int)Math.Round(Tasks.Average(t => t.Progress));
    }

    private static int CalculateWorkers(double cpu, double memory)
    {
        int cores = Math.Max(1, Environment.ProcessorCount);
        if (cpu < 30 && memory < 70) return Math.Clamp(cores / 2, 2, 6);
        if (cpu < 55 && memory < 80) return Math.Clamp(cores / 3, 2, 4);
        if (cpu < 75 && memory < 88) return 2;
        return 1;
    }

    private static string WorkerMode(double cpu, double memory)
    {
        if (cpu < 30 && memory < 70) return "Mode Performance";
        if (cpu < 55 && memory < 80) return "Mode Équilibré";
        if (cpu < 75 && memory < 88) return "Mode Protection";
        return "Mode Sécurité maximale";
    }

    private int ComputeScore()
    {
        int warnings = Logs.Count(l => l.Level.Equals("WARN", StringComparison.OrdinalIgnoreCase));
        int errors = Logs.Count(l => l.Level.Equals("ERROR", StringComparison.OrdinalIgnoreCase));
        return Math.Clamp(100 - warnings * 5 - errors * 15, 0, 100);
    }

    private string CreateReport(IReadOnlyList<AiRecommendation> recommendations, int score, int maxWorkers, double cpuStart, double memoryStart)
    {
        string folder = Path.Combine("C:\\Temp", "OptimisationWindows");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Rapport_Adaptive_{Environment.MachineName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.html");

        string taskRows = string.Join(string.Empty, Tasks.Select(task =>
            $"<tr><td>{WebUtility.HtmlEncode(task.Name)}</td><td>{WebUtility.HtmlEncode(task.Status)}</td><td>{task.Progress}%</td><td>{WebUtility.HtmlEncode(task.Message)}</td></tr>"));
        string logRows = string.Join(string.Empty, Logs.Select(log =>
            $"<tr><td>{log.Time:HH:mm:ss}</td><td>{WebUtility.HtmlEncode(log.Level)}</td><td>{WebUtility.HtmlEncode(log.Step)}</td><td>{WebUtility.HtmlEncode(log.Message)}</td></tr>"));
        string aiRows = string.Join(string.Empty, recommendations.Select(item =>
            $"<tr><td>{WebUtility.HtmlEncode(item.Severity)}</td><td>{WebUtility.HtmlEncode(item.Title)}</td><td>{WebUtility.HtmlEncode(item.Detail)}</td></tr>"));

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html lang=\"fr\"><head><meta charset=\"UTF-8\"><title>EPF Optimizer Pro v3.6</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial;background:#0f172a;color:#e5e7eb;margin:0}.wrap{padding:28px}.card{background:#111827;border:1px solid #334155;border-radius:16px;padding:18px;margin:14px 0}h1{color:#38bdf8}table{width:100%;border-collapse:collapse}th{background:#1e293b;color:#e0f2fe}td,th{padding:8px;border-top:1px solid #334155;vertical-align:top}</style></head><body><div class=\"wrap\">");
        html.AppendLine($"<h1>EPF Optimizer Pro Premium v3.6</h1><p>Score : <b>{score}/100</b> | Workers : <b>{maxWorkers}</b> | CPU initial : {cpuStart:0}% | RAM initiale : {memoryStart:0}%</p>");
        html.AppendLine("<div class=\"card\"><h2>Tâches indépendantes</h2><table><tr><th>Tâche</th><th>Statut</th><th>Progression</th><th>Message</th></tr>" + taskRows + "</table></div>");
        html.AppendLine("<div class=\"card\"><h2>IA locale</h2><table><tr><th>Niveau</th><th>Sujet</th><th>Détail</th></tr>" + aiRows + "</table></div>");
        html.AppendLine("<div class=\"card\"><h2>Journal</h2><table><tr><th>Heure</th><th>Niveau</th><th>Étape</th><th>Message</th></tr>" + logRows + "</table></div>");
        html.AppendLine("</div></body></html>");
        File.WriteAllText(path, html.ToString(), Encoding.UTF8);
        return path;
    }

    private static string FirstLine(string text)
    {
        return text.Split(new[] { "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Terminé";
    }

    private static bool IsWarning(string text)
    {
        return text.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("accès refusé", StringComparison.OrdinalIgnoreCase)
            || text.Contains("denied", StringComparison.OrdinalIgnoreCase)
            || text.Contains("erreur", StringComparison.OrdinalIgnoreCase)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("exception", StringComparison.OrdinalIgnoreCase);
    }

    private static string FriendlyWarning(string text)
    {
        if (text.Contains("access denied", StringComparison.OrdinalIgnoreCase) || text.Contains("denied", StringComparison.OrdinalIgnoreCase))
        {
            return "Accès refusé. Lance l'application en administrateur.";
        }

        return text;
    }

    private void Log(string level, string step, string message)
    {
        var log = new LogEntry { Level = level, Step = step, Message = message };
        Logs.Add(log);
        if (Logs.Count > 500)
        {
            Logs.RemoveRange(0, Logs.Count - 500);
        }
        LogWritten?.Invoke(log);
    }
}
