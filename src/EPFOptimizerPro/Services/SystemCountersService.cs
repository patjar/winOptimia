using System.Diagnostics;
using System.Globalization;

namespace EPFOptimizerPro.Services;

public sealed class SystemCountersService
{
    private DateTime _lastServiceRefresh = DateTime.MinValue;
    private int _runningServices;
    private int _restingServices;

    public int GetOpenHandleCount()
    {
        int total = 0;

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                total += process.HandleCount;
            }
            catch
            {
                // Certains processus système refusent l'accès. On les ignore.
            }
            finally
            {
                process.Dispose();
            }
        }

        return total;
    }

    public (int Running, int Resting) GetServiceCounts()
    {
        if ((DateTime.Now - _lastServiceRefresh).TotalSeconds < 10)
        {
            return (_runningServices, _restingServices);
        }

        RefreshServices();
        return (_runningServices, _restingServices);
    }

    private void RefreshServices()
    {
        try
        {
            using Process process = new();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"Get-Service | Group-Object Status | ForEach-Object { '{0}={1}' -f $_.Name,$_.Count }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            int running = 0;
            int resting = 0;

            foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('=', 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                string status = parts[0].Trim();
                if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    continue;
                }

                if (status.Equals("Running", StringComparison.OrdinalIgnoreCase))
                {
                    running += count;
                }
                else
                {
                    resting += count;
                }
            }

            _runningServices = running;
            _restingServices = resting;
            _lastServiceRefresh = DateTime.Now;
        }
        catch
        {
            // L'indicateur est informatif : en cas d'échec, on conserve les dernières valeurs connues.
        }
    }
}
