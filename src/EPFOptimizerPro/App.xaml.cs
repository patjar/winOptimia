using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EPFOptimizerPro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);

        MessageBox.Show(
            "Une erreur a été interceptée par la protection anti-crash." + Environment.NewLine + Environment.NewLine +
            e.Exception.Message + Environment.NewLine + Environment.NewLine +
            "Un journal a été écrit dans C:\\ProgramData\\EPFOptimizerPro\\crash.log.",
            "EPF Optimizer Pro - Protection anti-crash",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash("UnhandledException", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string source, Exception? exception)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "EPFOptimizerPro");

            Directory.CreateDirectory(folder);

            string file = Path.Combine(folder, "crash.log");
            var sb = new StringBuilder();
            sb.AppendLine("============================================================");
            sb.AppendLine($"Date       : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Source     : {source}");
            sb.AppendLine($"Message    : {exception?.Message ?? "Exception non disponible"}");
            sb.AppendLine("Details    :");
            sb.AppendLine(exception?.ToString() ?? "Aucun detail disponible.");
            sb.AppendLine();

            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // Ne jamais provoquer un deuxième crash pendant la journalisation.
        }
    }
}
