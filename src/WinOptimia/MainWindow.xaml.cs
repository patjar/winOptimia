using System.IO;
using System.Windows;
using WinOptimia.Models;
using WinOptimia.Services;

namespace WinOptimia;

public partial class MainWindow : Window
{
    private readonly GitHubUpdateService _updateService = new();
    private UpdateCheckResult? _lastCheck;
    private CancellationTokenSource? _cts;
    private readonly MemoryManager _memoryManager = new();
    private MemoryData _memory = new();

    public MainWindow()
    {
        InitializeComponent();
        TxtLocalVersion.Text = "Version locale : " + _updateService.CurrentVersion;
        Log("Application prête. Dépôt GitHub cible : patjar/winOptimia.");
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            string channel = RadioBeta.IsChecked == true ? "beta" : "stable";
            TxtChannel.Text = "Canal : " + channel;
            TxtUpdateStatus.Text = "Vérification GitHub en cours...";
            Log("Vérification GitHub sur le canal " + channel + ".");

            _lastCheck = await _updateService.CheckAsync(channel, _cts.Token);
            TxtLocalVersion.Text = "Version locale : " + _lastCheck.CurrentVersion;
            TxtRemoteVersion.Text = "Version GitHub : " + _lastCheck.LatestVersion;

            if (_lastCheck.UpdateAvailable)
            {
                TxtUpdateStatus.Text = "Mise à jour disponible : " + _lastCheck.LatestVersion;
                BtnDownload.IsEnabled = _lastCheck.Asset is not null;
                Log("Mise à jour disponible : " + _lastCheck.LatestVersion + ".");
                if (_lastCheck.Asset is null)
                {
                    Log("Aucun fichier ZIP trouvé dans les assets de la Release.");
                    TxtUpdateStatus.Text += " Aucun ZIP trouvé dans la Release.";
                }
            }
            else
            {
                TxtUpdateStatus.Text = "Aucune mise à jour disponible.";
                BtnDownload.IsEnabled = false;
                Log("Aucune mise à jour disponible.");
            }

            BtnOpenRelease.IsEnabled = !string.IsNullOrWhiteSpace(_lastCheck.ReleaseUrl);
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "Erreur GitHub : " + ex.Message;
            Log("Erreur GitHub : " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_lastCheck?.Asset is null || _cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            ProgressDownload.Value = 0;
            TxtUpdateStatus.Text = "Téléchargement de la mise à jour...";
            Log("Téléchargement : " + _lastCheck.Asset.Name);

            Progress<double> progress = new(value => ProgressDownload.Value = value);
            string zipPath = await _updateService.DownloadAsync(_lastCheck.Asset, progress, _cts.Token);
            Log("Fichier téléchargé : " + zipPath);

            bool updaterLaunched = _updateService.TryLaunchUpdater(zipPath);
            if (updaterLaunched)
            {
                Log("Updater lancé. Fermeture de winOptimia.");
                Application.Current.Shutdown();
            }
            else
            {
                TxtUpdateStatus.Text = "ZIP téléchargé. Updater absent dans ce build, dossier ouvert.";
                Log("Updater absent. Le dossier de téléchargement a été ouvert.");
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "Erreur téléchargement : " + ex.Message;
            Log("Erreur téléchargement : " + ex.Message);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void BtnOpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_lastCheck?.ReleaseUrl))
        {
            _updateService.OpenReleasePage(_lastCheck.ReleaseUrl);
        }
    }

    private void SetBusy(bool busy)
    {
        BtnCheckUpdate.IsEnabled = !busy;
        RadioStable.IsEnabled = !busy;
        RadioBeta.IsEnabled = !busy;
    }

    private void Log(string message)
    {
        TxtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
        TxtLog.ScrollToEnd();
    }
}
