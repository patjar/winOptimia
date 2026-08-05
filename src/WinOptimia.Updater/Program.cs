using System.Diagnostics;
using System.IO.Compression;

static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static int? GetIntArg(string[] args, string name)
{
    string? value = GetArg(args, name);
    return int.TryParse(value, out int result) ? result : null;
}

string? zipPath = GetArg(args, "--zip");
string? targetFolder = GetArg(args, "--target");
string? exeName = GetArg(args, "--exe") ?? "WinOptimia.exe";
int? waitProcessId = GetIntArg(args, "--wait");

if (string.IsNullOrWhiteSpace(zipPath) || string.IsNullOrWhiteSpace(targetFolder))
{
    Console.WriteLine("Arguments manquants.");
    return 2;
}

if (waitProcessId.HasValue)
{
    try
    {
        Process process = Process.GetProcessById(waitProcessId.Value);
        process.WaitForExit(30000);
    }
    catch
    {
    }
}

string tempFolder = Path.Combine(Path.GetTempPath(), "winOptimia-update-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempFolder);
ZipFile.ExtractToDirectory(zipPath, tempFolder, overwriteFiles: true);

foreach (string file in Directory.EnumerateFiles(tempFolder, "*", SearchOption.AllDirectories))
{
    string relative = Path.GetRelativePath(tempFolder, file);
    string destination = Path.Combine(targetFolder, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

    string filename = Path.GetFileName(destination);
    if (filename.Equals("WinOptimia.Updater.exe", StringComparison.OrdinalIgnoreCase))
    {
        continue;
    }

    File.Copy(file, destination, overwrite: true);
}

string exePath = Path.Combine(targetFolder, exeName);
if (File.Exists(exePath))
{
    Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
}

return 0;
