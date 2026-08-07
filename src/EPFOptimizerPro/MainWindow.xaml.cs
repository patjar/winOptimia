using System.Text;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using EPFOptimizerPro.Models;
using EPFOptimizerPro.Services;

namespace EPFOptimizerPro;

public partial class MainWindow : Window
{
    private readonly SystemMetrics _metrics = new();
    private readonly SystemCountersService _systemCounters = new();
    private readonly AiAdvisorService _aiAdvisor = new();
    private readonly HealthScoreService _healthScores = new();
    private readonly AiScoreHistoryService _aiHistory = new();
    private readonly AiMemoryReportService _aiMemoryReport = new();
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _adminBlinkTimer = new();
    private bool _adminBlinkState;
    private readonly string[] _frames = { "◐", "◓", "◑", "◒" };
    private readonly GitHubUpdateService _updateService = new();
    private AdaptiveTaskEngine _engine;
    private CancellationTokenSource? _cts;
    private UpdateCheckResult? _lastCheck = null;
#pragma warning disable CS0649
    private CancellationTokenSource? _updateCts;
#pragma warning restore CS0649
    private UpdateCheckResult? _lastUpdateCheck;
    private int _frameIndex;
    private int _lastWorkerCount;
    private string _lastWorkerMode = "non initialise";
    private string? _lastReport;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new AdaptiveTaskEngine(Dispatcher);
        WireEngine();
        ActiveTasksItems.ItemsSource = _engine.ActiveTasks;
        CompletedTasksItems.ItemsSource = _engine.CompletedTasks;
        UpdateDashboardSummary();
        UpdateSystemCounters();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        _adminBlinkTimer.Interval = TimeSpan.FromMilliseconds(650);
        _adminBlinkTimer.Tick += (_, _) => AdminBlinkTick();
        RenderRecommendations(_engine.CurrentRecommendations);
        ShowStartupAdvice();
        UpdateAdminVisualStatus();
        RenderAiAdvisor(0, 0);
        TxtUpdateStatus.Text = "Mise à jour : non vérifiée";
    }

    private void ShowStartupAdvice()
    {
        TxtActionHint.Text = "Conseil : lancez Audit seul pour analyser le poste, ou Optimiser pour corriger automatiquement.";
        TxtStep.Text = "Conseil de démarrage";
        TxtPercent.Text = "0 %";
        ProgressGlobal.Value = 0;

        if (TxtAi.Text.Length == 0)
        {
            TxtAi.Text = "Conseil de démarrage\n\nLancez Audit seul pour obtenir un diagnostic du poste. Utilisez Optimiser quand vous voulez appliquer les corrections automatiquement.\n";
        }
    }

    private void UpdateSystemCounters()
    {
        try
        {
            int openHandles = _systemCounters.GetOpenHandleCount();
            var services = _systemCounters.GetServiceCounts();

            TxtFilesCard.Text = openHandles.ToString("N0");
            ProgressFilesMini.Value = Math.Clamp(openHandles / 2000.0, 0, 100);

            TxtServicesCard.Text = services.Running + " / " + services.Resting;
            int total = services.Running + services.Resting;
            ProgressServicesMini.Value = total <= 0 ? 0 : Math.Clamp(services.Running * 100.0 / total, 0, 100);
        }
        catch
        {
            // Indicateurs informatifs uniquement : aucune erreur visuelle ne doit bloquer l'application.
        }
    }
    private void WireEngine()
    {
        _engine.GlobalProgressChanged += OnGlobalProgressChanged;
        _engine.LogWritten += OnLogWritten;
        _engine.RecommendationsUpdated += items => Dispatcher.Invoke(() => RenderRecommendations(items));
        _engine.ScoreUpdated += score => Dispatcher.Invoke(() => UpdateScoreHero(score));
                _engine.WorkerModeChanged += (count, mode) =>
        {
            _lastWorkerCount = count;
            _lastWorkerMode = mode;
            Dispatcher.Invoke(() => TxtWorkers.Text = $"Workers : {count} | {mode}");
        };
    }

    private void Tick()
    {
        _frameIndex = (_frameIndex + 1) % _frames.Length;
        double cpu = _metrics.CpuPercent();
        double ram = _metrics.MemoryPercent();
        TxtClock.Text = $"{_frames[_frameIndex]} {DateTime.Now:HH:mm:ss}";
        TxtMetrics.Text = $"CPU {cpu:0} % | RAM {ram:0} %";
        ProgressCpuMini.Value = cpu;
        ProgressRamMini.Value = ram;
        TxtCpuCard.Text = $"{cpu:0} %";
        TxtRamCard.Text = $"{ram:0} %";
        UpdateDashboardSummary();
        UpdateSystemCounters();
    }

    private async void BtnAudit_Click(object sender, RoutedEventArgs e) => await RunAsync(false);
    private async void BtnOptimize_Click(object sender, RoutedEventArgs e) => await RunAsync(true);

    private async Task RunAsync(bool optimize)
    {
        if (_cts is not null) return;

        _cts = new CancellationTokenSource();
        TxtLog.Clear();
        SetRunningVisualState(optimize);

        double cpuStart = _metrics.CpuPercent();
        double memoryStart = _metrics.MemoryPercent();

        try
        {
            _lastReport = await _engine.RunAsync(optimize, _cts.Token, cpuStart, memoryStart);
            TxtActionHint.Text = "Tâches terminées. Rapport disponible.";
            RefreshAiDashboardV2(cpuStart, memoryStart);
        }
        catch (OperationCanceledException)
        {
            Append("[WARN] Opération annulée.");
            TxtActionHint.Text = "Opération annulée.";
        }
        finally
        {
            ResetButtonVisualState();
            _cts.Dispose();
            _cts = null;
        }
    }

    private void OnGlobalProgressChanged(int percent, string step)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressGlobal.Value = percent;
            TxtPercent.Text = percent + " %";
            TxtStep.Text = step;
        });
    }

    private void OnLogWritten(LogEntry entry)
    {
        Dispatcher.Invoke(() =>
        {
            Append(entry.ToString());
            TxtEvents.Text = _engine.Logs.Count <= 1 ? "1 événement" : $"{_engine.Logs.Count} événements";
        });
    }

    private void UpdateDashboardSummary()
    {
        int total = _engine.Tasks.Count;
        int done = _engine.CompletedTasks.Count(t => t.Status.Equals("Terminé", StringComparison.OrdinalIgnoreCase));
        int running = _engine.ActiveTasks.Count(t => t.Status.Equals("En cours", StringComparison.OrdinalIgnoreCase));
        int waiting = _engine.ActiveTasks.Count(t => t.Status.Equals("En attente", StringComparison.OrdinalIgnoreCase));
        int warn = _engine.CompletedTasks.Count(t => t.Status.Equals("Avertissement", StringComparison.OrdinalIgnoreCase));
        int error = _engine.CompletedTasks.Count(t => t.Status.Equals("Erreur", StringComparison.OrdinalIgnoreCase));
        string activeNames = string.Join(", ", _engine.ActiveTasks.Where(t => t.Status.Equals("En cours", StringComparison.OrdinalIgnoreCase)).Select(t => $"{t.Name} {t.Progress}%"));
        if (string.IsNullOrWhiteSpace(activeNames))
        {
            activeNames = "aucune tâche active";
        }

        bool hasActiveVisibleTasks = _engine.ActiveTasks.Any(t =>
            t.Status.Equals("En cours", StringComparison.OrdinalIgnoreCase) ||
            t.Status.Equals("En attente", StringComparison.OrdinalIgnoreCase));

        TxtNoActiveTasks.Visibility = hasActiveVisibleTasks
            ? Visibility.Collapsed
            : Visibility.Visible;
        TxtDashboardSummary.Text = $"  |  {done}/{total} terminées  |  {running} en cours : {activeNames}  |  {waiting} attente  |  {warn} avert.  |  {error} erreur";
    }


    private void RenderAiAdvisor(double cpuInitial, double ramInitial)
    {
        int score = 0;
        _ = int.TryParse(TxtScoreHero.Text, out score);

        var tips = _aiAdvisor.Analyze(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode,
            cpuInitial,
            ramInitial);

        TxtAi.Clear();
                TxtAi.AppendText(_aiAdvisor.RenderText(tips));

        var health = _healthScores.Compute(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode);

        TxtAi.AppendText(Environment.NewLine);
        TxtAi.AppendText("Scores IA par categorie" + Environment.NewLine);
        TxtAi.AppendText("-----------------------" + Environment.NewLine);
        TxtAi.AppendText(_healthScores.RenderText(health));
        TxtAi.AppendText(Environment.NewLine);
        TxtAi.ScrollToHome();
    }
            private void RenderRecommendations(IReadOnlyList<AiRecommendation> recommendations)
    {
        TxtAi.Clear();
        TxtAi.AppendText("Assistant IA local" + Environment.NewLine);
        TxtAi.AppendText("==================" + Environment.NewLine + Environment.NewLine);
        TxtAi.AppendText("Lance un audit ou une optimisation pour générer la synthèse IA." + Environment.NewLine + Environment.NewLine);

        foreach (var item in recommendations)
        {
            TxtAi.AppendText($"[{item.Severity}] {item.Title}" + Environment.NewLine);
            TxtAi.AppendText(item.Detail + Environment.NewLine + Environment.NewLine);
        }

        TxtAi.ScrollToHome();
    }
            private void RefreshAiDashboard(double cpuStart, double memoryStart)
    {
        int score = 0;
        _ = int.TryParse(TxtScoreHero.Text, out score);

        var health = _healthScores.Compute(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode);

        _aiHistory.SaveSnapshot(health, _lastWorkerCount, _lastWorkerMode);

        TxtAiHeadline.Text = $"Santé IA : {health.Global}/100";
        TxtAiSubScore.Text = $"Perf {health.Performance} | Sécu {health.Security} | Stockage {health.Storage} | Update {health.WindowsUpdate} | Stabilité {health.Stability}";

        var tips = _aiAdvisor.Analyze(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode,
            cpuStart,
            memoryStart);

        TxtAi.Clear();
        TxtAi.AppendText("Synthèse IA détaillée" + Environment.NewLine);
        TxtAi.AppendText("====================" + Environment.NewLine + Environment.NewLine);
        TxtAi.AppendText(_healthScores.RenderText(health));
        TxtAi.AppendText(Environment.NewLine);
        TxtAi.AppendText(Environment.NewLine);
        TxtAi.AppendText("Conseils" + Environment.NewLine);
        TxtAi.AppendText("--------" + Environment.NewLine);
        TxtAi.AppendText(_aiAdvisor.RenderText(tips));
        TxtAi.ScrollToHome();
    }

    private void RefreshAiDashboardV2(double cpuStart, double memoryStart)
    {
        int score = 0;
        _ = int.TryParse(TxtScoreHero.Text, out score);

        var health = _healthScores.Compute(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode);

        _aiHistory.SaveSnapshot(health, _lastWorkerCount, _lastWorkerMode);
        string trendText = _aiHistory.GetTrendText();

        TxtAiHeadline.Text = $"Santé IA : {health.Global}/100";
        TxtAiSubScore.Text = $"Perf {health.Performance} | Sécu {health.Security} | Stockage {health.Storage} | Update {health.WindowsUpdate} | Stabilité {health.Stability}";
        TxtAiAdvice.Text = health.Summary + Environment.NewLine + trendText;

        var tips = _aiAdvisor.Analyze(
            _engine.Logs.Cast<object>(),
            _engine.CompletedTasks.Cast<object>(),
            score,
            _lastWorkerCount,
            _lastWorkerMode,
            cpuStart,
            memoryStart);

        TxtAi.Clear();
        TxtAi.AppendText("Synthèse IA détaillée" + Environment.NewLine);
        TxtAi.AppendText("====================" + Environment.NewLine + Environment.NewLine);
        TxtAi.AppendText(_healthScores.RenderText(health));
        TxtAi.AppendText(Environment.NewLine);
        TxtAi.AppendText(trendText + Environment.NewLine);
        TxtAi.AppendText(Environment.NewLine);
        TxtAi.AppendText("Conseils" + Environment.NewLine);
        TxtAi.AppendText("--------" + Environment.NewLine);
        TxtAi.AppendText(_aiAdvisor.RenderText(tips));
        TxtAi.ScrollToHome();
    }
    private void Append(string text)
    {
        TxtLog.AppendText(text + Environment.NewLine);
        const int maxCharacters = 60000;
        if (TxtLog.Text.Length > maxCharacters)
        {
            TxtLog.Text = TxtLog.Text[^maxCharacters..];
        }
        TxtLog.ScrollToEnd();
    }

    private void SetRunningVisualState(bool optimize)
    {
        BtnAudit.IsEnabled = false;
        BtnOptimize.IsEnabled = false;
        BtnCheckUpdate.IsEnabled = false;
        BtnDownloadUpdate.IsEnabled = false;
        BtnAudit.Background = BrushFromHex(optimize ? "#1E3A8A" : "#38BDF8");
        BtnOptimize.Background = BrushFromHex(optimize ? "#22C55E" : "#1E3A8A");
        BtnCancel.Background = BrushFromHex("#F59E0B");
        TxtActionHint.Text = optimize ? "Optimisation adaptative en cours." : "Audit adaptatif en cours.";
    }

    private void ResetButtonVisualState()
    {
        BtnAudit.IsEnabled = true;
        BtnOptimize.IsEnabled = true;
        BtnCheckUpdate.IsEnabled = true;
        BtnAudit.Background = BrushFromHex("#2563EB");
        BtnOptimize.Background = BrushFromHex("#2563EB");
        BtnCancel.Background = BrushFromHex("#2563EB");
    }

    private void UpdateScoreHero(int score)
    {
        TxtScoreHero.Text = score.ToString();
        TxtScoreHero.Foreground = score >= 85 ? BrushFromHex("#22C55E") : score >= 65 ? BrushFromHex("#F59E0B") : BrushFromHex("#EF4444");
    }

        private void UpdateAdminVisualStatus()
    {
        bool isAdmin = IsRunningAsAdministrator();

        TxtAdminStatus.Text = isAdmin ? "Admin : oui" : "Admin : non";

        if (isAdmin)
        {
            _adminBlinkTimer.Stop();
            TxtAdminStatus.Visibility = Visibility.Visible;
            TxtAdminStatus.Foreground = BrushFromHex("#22C55E");
            Append("[INFO] Application lancée avec privilèges administrateur.");
        }
        else
        {
            TxtAdminStatus.Visibility = Visibility.Visible;
            TxtAdminStatus.Foreground = BrushFromHex("#EF4444");
            _adminBlinkState = true;
            _adminBlinkTimer.Start();
            Append("[WARN] Application lancée sans privilèges administrateur. Certaines optimisations système peuvent être limitées.");
            TxtActionHint.Text = "Mode non administrateur : certaines optimisations système peuvent être limitées.";
        }
    }

    private void AdminBlinkTick()
    {
        if (TxtAdminStatus.Text != "Admin : non")
        {
            _adminBlinkTimer.Stop();
            TxtAdminStatus.Visibility = Visibility.Visible;
            return;
        }

        _adminBlinkState = !_adminBlinkState;
        TxtAdminStatus.Foreground = _adminBlinkState ? BrushFromHex("#EF4444") : BrushFromHex("#7F1D1D");
    }
    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        BtnCancel.Background = BrushFromHex("#EF4444");
        TxtActionHint.Text = "Annulation demandée.";
        _cts?.Cancel();
        _updateCts?.Cancel();
    }

    private void BtnOpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastReport) && File.Exists(_lastReport))
        {
            Process.Start(new ProcessStartInfo(_lastReport) { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show("Aucun rapport disponible.", "EPF Optimizer Pro", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

                private void BtnOpenLearning_Click(object sender, RoutedEventArgs e)
    {
        var window = new AiCenterWindow
        {
            Owner = this
        };

        window.ShowDialog();
        Append("[INFO] Centre IA ouvert.");
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null) return;

        BtnCheckUpdate.IsEnabled = false;
        BtnDownloadUpdate.IsEnabled = false;
        BtnOpenUpdateRelease.IsEnabled = false;
        TxtUpdateStatus.Text = "Mise à jour : vérification GitHub...";
        Append("[INFO] Vérification GitHub des mises à jour...");

        try
        {
            _lastUpdateCheck = await _updateService.CheckLatestAsync(CancellationToken.None);
            TxtUpdateStatus.Text = _lastUpdateCheck.UpdateAvailable
                ? $"Mise à jour disponible : {_lastUpdateCheck.LatestVersion}"
                : $"Mise à jour : OK ({_lastUpdateCheck.CurrentVersion})";
            BtnOpenUpdateRelease.IsEnabled = !string.IsNullOrWhiteSpace(_lastUpdateCheck.ReleaseUrl);
            BtnDownloadUpdate.IsEnabled = _lastUpdateCheck.UpdateAvailable && _lastUpdateCheck.Asset is not null;

            if (_lastUpdateCheck.UpdateAvailable)
            {
                Append($"[OK] Mise à jour disponible : {_lastUpdateCheck.LatestVersion}");
            }
            else
            {
                Append($"[OK] Aucune mise à jour disponible. Version locale : {_lastUpdateCheck.CurrentVersion}, GitHub : {_lastUpdateCheck.LatestVersion}");
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "Mise à jour : erreur GitHub";
            Append("[ERROR] Erreur GitHub update : " + ex.Message);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }
        private async void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var updateCts = new CancellationTokenSource(TimeSpan.FromMinutes(10));

            // Si l'etat de verification a ete perdu, on relance une verification GitHub automatiquement.
            if (_lastCheck?.Asset is null)
            {
                Append("[INFO] Etat update absent : verification GitHub relancee avant installation.");
                _lastCheck = await _updateService.CheckLatestAsync(updateCts.Token);
                RefreshInstallerUpdateButton();
            }

            if (_lastCheck?.Asset is null)
            {
                MessageBox.Show(
                    "Aucun package de mise a jour disponible. La verification GitHub n'a retourne aucun asset MSI/ZIP utilisable.",
                    "EPF Optimizer Pro",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var asset = _lastCheck.Asset;
            string assetName = asset.Name ?? string.Empty;
            bool isMsi = assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

            BtnDownloadUpdate.IsEnabled = false;
            TxtStep.Text = isMsi ? "Telechargement de l'installateur..." : "Telechargement de la mise a jour...";
            TxtPercent.Text = "0 %";
            ProgressGlobal.Value = 0;

            Append("[INFO] Telechargement update : " + assetName);

            var progress = new Progress<double>(value =>
            {
                double safeValue = Math.Max(0, Math.Min(100, value));
                ProgressGlobal.Value = safeValue;
                TxtPercent.Text = safeValue.ToString("0") + " %";
            });

            string filePath = await _updateService.DownloadAsync(asset, progress, updateCts.Token);
            Append("[OK] Fichier telecharge : " + filePath);
            TxtStep.Text = "Update telechargee";

            if (filePath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                var answer = MessageBox.Show(
                    "La mise a jour MSI a ete telechargee. Voulez-vous lancer l'installation maintenant ?\n\n" + filePath,
                    "Installer update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Yes)
                {
                    Append("[INFO] Lancement de msiexec pour installer la mise a jour.");

                    LaunchMsiInstallerAndRestart(filePath);
                    Application.Current.Shutdown();
                    return;
                }

                Append("[INFO] Installation differee par l'utilisateur. Ouverture du dossier update.");
                OpenUpdateFolderSafe(filePath);
                return;
            }

            Append("[INFO] Package non MSI. Ouverture du dossier update.");
            OpenUpdateFolderSafe(filePath);
        }
        catch (OperationCanceledException)
        {
            Append("[WARN] Telechargement de la mise a jour annule.");
        }
        catch (Exception ex)
        {
            Append("[ERREUR] Installation update : " + ex.Message);
            MessageBox.Show(
                "Impossible de lancer l'installation de la mise a jour :\n\n" + ex.Message,
                "EPF Optimizer Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            BtnDownloadUpdate.IsEnabled = _lastCheck?.Asset is not null;
            RefreshInstallerUpdateButton();
        }
    }


    private void BtnOpenUpdateRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastUpdateCheck?.ReleaseUrl))
        {
            Process.Start(new ProcessStartInfo(_lastUpdateCheck.ReleaseUrl) { UseShellExecute = true });
        }
    }

    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _updateCts?.Cancel();
        Close();
    }


    private void RefreshInstallerUpdateButton()
    {
        try
        {
            if (_lastCheck?.Asset is null)
            {
                BtnDownloadUpdate.IsEnabled = false;
                BtnDownloadUpdate.Content = "Télécharger update";
                return;
            }

            BtnDownloadUpdate.IsEnabled = _lastCheck.UpdateAvailable;

            string assetName = _lastCheck.Asset.Name ?? string.Empty;
            if (assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            {
                BtnDownloadUpdate.Content = "Installer update";
            }
            else
            {
                BtnDownloadUpdate.Content = "Télécharger update";
            }
        }
        catch (Exception ex)
        {
            Append("[WARN] Impossible de mettre Ã  jour le bouton update : " + ex.Message);
        }
    }

    private void OpenUpdateFolderSafe(string? downloadedFilePath = null)
    {
        try
        {
            string folder;

            if (!string.IsNullOrWhiteSpace(downloadedFilePath) && File.Exists(downloadedFilePath))
            {
                folder = Path.GetDirectoryName(downloadedFilePath) ?? string.Empty;
            }
            else
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "WinOptimia",
                    "Updates");
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WinOptimia",
                    "Updates");
            }

            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Impossible dâ€™ouvrir le dossier des mises Ã  jour : " + ex.Message,
                "EPF Optimizer Pro",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _cts?.Dispose();
        base.OnClosed(e);
    }

    private void LaunchMsiInstallerAndRestart(string msiPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(msiPath) || !File.Exists(msiPath))
            {
                MessageBox.Show("Le fichier MSI de mise a jour est introuvable.", "EPF Optimizer Pro", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string programData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "EPFOptimizerPro");

            Directory.CreateDirectory(programData);

            string scriptPath = Path.Combine(programData, "install-update-and-restart.ps1");
            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

            string EscapeForPowerShell(string value)
            {
                return (value ?? string.Empty).Replace("'", "''");
            }

            string[] scriptLines =
            {
                "$ErrorActionPreference = 'SilentlyContinue'",
                "$msi = '" + EscapeForPowerShell(msiPath) + "'",
                "$currentExe = '" + EscapeForPowerShell(currentExe) + "'",
                "Start-Sleep -Seconds 1",
                "$arguments = '/i \"' + $msi + '\"'",
                "$p = Start-Process msiexec.exe -ArgumentList $arguments -Wait -PassThru",
                "$paths = @($currentExe, 'C:\\Program Files\\EPF Optimizer Pro\\EPFOptimizerPro.exe', 'C:\\Program Files\\EPFOptimizerPro\\EPFOptimizerPro.exe', 'C:\\Program Files (x86)\\EPF Optimizer Pro\\EPFOptimizerPro.exe', 'C:\\Program Files (x86)\\EPFOptimizerPro\\EPFOptimizerPro.exe')",
                "$target = $paths | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1",
                "if ($target) { Start-Process $target }"
            };

            File.WriteAllLines(scriptPath, scriptLines);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-ExecutionPolicy Bypass -File \"" + scriptPath + "\"",
                UseShellExecute = true,
                Verb = "runas"
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Impossible de lancer l'installation MSI : " + ex.Message, "EPF Optimizer Pro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

























