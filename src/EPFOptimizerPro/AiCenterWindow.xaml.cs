using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace EPFOptimizerPro;

public partial class AiCenterWindow : Window
{
    private readonly string _folder;
    private readonly string _historyFile;
    private readonly string _learningFile;
    private readonly string _summaryFile;

    public AiCenterWindow()
    {
        InitializeComponent();

        _folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "EPFOptimizerPro");

        Directory.CreateDirectory(_folder);

        _historyFile = Path.Combine(_folder, "ai_score_history.json");
        _learningFile = Path.Combine(_folder, "learning.json");
        _summaryFile = Path.Combine(_folder, "ai_memory_summary.html");

        EnsureFiles();
        RefreshView();
    }

    private void EnsureFiles()
    {
        if (!File.Exists(_historyFile))
        {
            File.WriteAllText(_historyFile, "[]", Encoding.UTF8);
        }

        if (!File.Exists(_learningFile))
        {
            File.WriteAllText(_learningFile, "{}", Encoding.UTF8);
        }
    }

    private void RefreshView()
    {
        EnsureFiles();

        var memory = ReadMemory();
        string plainSummary = BuildPlainSummary(memory);
        string htmlSummary = BuildHtmlSummary(memory);

        File.WriteAllText(_summaryFile, htmlSummary, Encoding.UTF8);
        TxtSummary.Text = plainSummary;

        TxtFiles.Text =
            "Dossier memoire : " + _folder + Environment.NewLine +
            "Historique IA : " + _historyFile + Environment.NewLine +
            "Learning : " + _learningFile + Environment.NewLine +
            "Rapport HTML : " + _summaryFile + Environment.NewLine + Environment.NewLine +
            "Taille historique : " + FileSizeText(_historyFile) + Environment.NewLine +
            "Taille learning : " + FileSizeText(_learningFile) + Environment.NewLine +
            "Taille rapport HTML : " + FileSizeText(_summaryFile);
    }

    private AiMemorySnapshot ReadMemory()
    {
        using JsonDocument? doc = LoadHistoryDocument();
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new AiMemorySnapshot();
        }

        var items = doc.RootElement.EnumerateArray().ToList();
        var snapshot = new AiMemorySnapshot
        {
            Count = items.Count
        };

        if (items.Count == 0)
        {
            return snapshot;
        }

        JsonElement last = items[^1];
        snapshot.Global = GetInt(last, "Global");
        snapshot.Performance = GetInt(last, "Performance");
        snapshot.Security = GetInt(last, "Security");
        snapshot.Storage = GetInt(last, "Storage");
        snapshot.WindowsUpdate = GetInt(last, "WindowsUpdate");
        snapshot.Stability = GetInt(last, "Stability");
        snapshot.WorkerCount = GetInt(last, "WorkerCount");
        snapshot.WorkerMode = GetString(last, "WorkerMode");
        snapshot.Date = GetString(last, "Date");
        snapshot.Trend = BuildTrend(items);

        return snapshot;
    }

    private string BuildPlainSummary(AiMemorySnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EPF Optimizer Pro - Centre IA");
        sb.AppendLine("=============================");
        sb.AppendLine($"Genere le : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"Instantanes IA : {snapshot.Count}");

        if (snapshot.Count == 0)
        {
            sb.AppendLine("Aucun instantane IA disponible pour le moment.");
            return sb.ToString();
        }

        sb.AppendLine($"Dernier score global : {snapshot.Global}/100");
        sb.AppendLine($"Performance : {snapshot.Performance}/100");
        sb.AppendLine($"Securite : {snapshot.Security}/100");
        sb.AppendLine($"Stockage : {snapshot.Storage}/100");
        sb.AppendLine($"Windows Update : {snapshot.WindowsUpdate}/100");
        sb.AppendLine($"Stabilite : {snapshot.Stability}/100");
        sb.AppendLine($"Workers observes : {snapshot.WorkerCount} ({snapshot.WorkerMode})");
        sb.AppendLine($"Dernier instantane : {snapshot.Date}");
        sb.AppendLine();
        sb.AppendLine(snapshot.Trend);
        return sb.ToString();
    }

    private string BuildHtmlSummary(AiMemorySnapshot snapshot)
    {
        static string H(string value) => WebUtility.HtmlEncode(value ?? string.Empty);

        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"fr\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("<title>Centre IA - EPF Optimizer Pro</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{margin:0;background:#020617;color:#f8fafc;font-family:Segoe UI,Arial,sans-serif;} .page{max-width:1100px;margin:0 auto;padding:28px;} .hero,.card{background:#111827;border:1px solid #26364f;border-radius:18px;padding:22px;margin-bottom:18px;box-shadow:0 10px 30px rgba(0,0,0,.25);} h1{color:#38bdf8;margin:0 0 6px;font-size:34px;} h2{color:#38bdf8;margin:0 0 12px;font-size:24px;} .muted{color:#94a3b8;} .grid{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;} .score{font-size:34px;color:#22c55e;font-weight:800;} .metric{background:#050a16;border:1px solid #26364f;border-radius:14px;padding:16px;} .metric b{color:#38bdf8;} .trend{border-left:4px solid #38bdf8;padding-left:14px;line-height:1.5;} code{color:#e5e7eb;background:#050a16;padding:2px 5px;border-radius:5px;} footer{color:#94a3b8;font-size:12px;margin-top:22px;} @media(max-width:850px){.grid{grid-template-columns:1fr;}} ");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body><main class=\"page\">");
        sb.AppendLine("<section class=\"hero\">");
        sb.AppendLine("<h1>Centre IA</h1>");
        sb.AppendLine("<div class=\"muted\">EPF Optimizer Pro - Rapport mémoire IA local</div>");
        sb.AppendLine($"<div class=\"muted\">Généré le {DateTime.Now:yyyy-MM-dd HH:mm:ss}</div>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Résumé</h2>");
        sb.AppendLine($"<div>Instantanés IA : <b>{snapshot.Count}</b></div>");

        if (snapshot.Count == 0)
        {
            sb.AppendLine("<p class=\"muted\">Aucun instantané IA disponible pour le moment.</p>");
        }
        else
        {
            sb.AppendLine($"<div class=\"score\">{snapshot.Global}/100</div>");
            sb.AppendLine($"<div class=\"muted\">Dernier score global</div>");
            sb.AppendLine("</section>");

            sb.AppendLine("<section class=\"grid\">");
            sb.AppendLine(Metric("Performance", snapshot.Performance));
            sb.AppendLine(Metric("Sécurité", snapshot.Security));
            sb.AppendLine(Metric("Stockage", snapshot.Storage));
            sb.AppendLine(Metric("Windows Update", snapshot.WindowsUpdate));
            sb.AppendLine(Metric("Stabilité", snapshot.Stability));
            sb.AppendLine($"<div class=\"metric\"><b>Workers</b><br>{snapshot.WorkerCount} ({H(snapshot.WorkerMode)})</div>");
            sb.AppendLine("</section>");

            sb.AppendLine("<section class=\"card\">");
            sb.AppendLine("<h2>Tendance</h2>");
            sb.AppendLine($"<p class=\"trend\">{H(snapshot.Trend)}</p>");
            sb.AppendLine($"<p class=\"muted\">Dernier instantané : {H(snapshot.Date)}</p>");
        }

        sb.AppendLine("</section>");
        sb.AppendLine("<section class=\"card\">");
        sb.AppendLine("<h2>Fichiers mémoire</h2>");
        sb.AppendLine($"<p>Historique IA : <code>{H(_historyFile)}</code></p>");
        sb.AppendLine($"<p>Learning : <code>{H(_learningFile)}</code></p>");
        sb.AppendLine($"<p>Rapport HTML : <code>{H(_summaryFile)}</code></p>");
        sb.AppendLine("</section>");
        sb.AppendLine("<footer>Rapport généré localement par EPF Optimizer Pro. Aucune donnée n'est envoyée en ligne.</footer>");
        sb.AppendLine("</main></body></html>");
        return sb.ToString();

        static string Metric(string label, int value)
        {
            return $"<div class=\"metric\"><b>{WebUtility.HtmlEncode(label)}</b><br><span class=\"score\" style=\"font-size:26px\">{value}/100</span></div>";
        }
    }

    private JsonDocument? LoadHistoryDocument()
    {
        try
        {
            string json = File.ReadAllText(_historyFile, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildTrend(List<JsonElement> items)
    {
        if (items.Count < 2)
        {
            return "Tendance : historique insuffisant pour comparer les exécutions.";
        }

        int window = Math.Min(5, items.Count);
        var recent = items.Skip(items.Count - window).ToList();
        int first = GetInt(recent[0], "Global");
        int last = GetInt(recent[^1], "Global");
        int delta = last - first;
        double average = recent.Average(x => GetInt(x, "Global"));

        if (delta >= 5)
        {
            return $"Tendance : amélioration (+{delta} points, moyenne récente {average:0}/100).";
        }

        if (delta <= -5)
        {
            return $"Tendance : dégradation ({delta} points, moyenne récente {average:0}/100).";
        }

        return $"Tendance : stable, moyenne récente {average:0}/100.";
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value) && value.TryGetInt32(out int result))
        {
            return result;
        }

        return 0;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out JsonElement value))
        {
            return value.ToString();
        }

        return string.Empty;
    }

    private static string FileSizeText(string path)
    {
        if (!File.Exists(path))
        {
            return "absent";
        }

        long bytes = new FileInfo(path).Length;
        return bytes < 1024 ? bytes + " o" : (bytes / 1024.0).ToString("0.0") + " Ko";
    }

    private void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                MessageBox.Show("Fichier introuvable : " + path, "Centre IA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string extension = Path.GetExtension(path);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = "\"" + path + "\"",
                    UseShellExecute = false
                });
                return;
            }

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible d'ouvrir le fichier." + Environment.NewLine + path + Environment.NewLine + Environment.NewLine + ex.Message,
                "Centre IA",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => RefreshView();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible d'ouvrir le dossier mémoire IA." + Environment.NewLine + _folder + Environment.NewLine + Environment.NewLine + ex.Message,
                "Centre IA",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnOpenHistory_Click(object sender, RoutedEventArgs e) => OpenFile(_historyFile);

    private void BtnOpenLearning_Click(object sender, RoutedEventArgs e) => OpenFile(_learningFile);

    private void BtnOpenSummary_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshView();
            OpenFile(_summaryFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible de générer ou d'ouvrir le rapport HTML IA." + Environment.NewLine + ex.Message,
                "Centre IA",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void BtnResetHistory_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "Voulez-vous vraiment réinitialiser l'historique IA ?",
            "Centre IA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        File.WriteAllText(_historyFile, "[]", Encoding.UTF8);
        RefreshView();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class AiMemorySnapshot
    {
        public int Count { get; set; }
        public int Global { get; set; }
        public int Performance { get; set; }
        public int Security { get; set; }
        public int Storage { get; set; }
        public int WindowsUpdate { get; set; }
        public int Stability { get; set; }
        public int WorkerCount { get; set; }
        public string WorkerMode { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Trend { get; set; } = string.Empty;
    }
}
