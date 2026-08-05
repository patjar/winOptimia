using System.Text.Json;

namespace WinOptimia.Services;

public class MemoryData
{
    public int RunCount { get; set; }

    public int BestWorkers { get; set; } = 4;

    public int LastScore { get; set; } = 0;
}

public class MemoryManager
{
    private readonly string _folder;
    private readonly string _file;

    public MemoryManager()
    {
        _folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.CommonApplicationData),
            "WinOptimia");

        Directory.CreateDirectory(_folder);

        _file = Path.Combine(_folder, "memory.json");
    }

    public MemoryData Load()
    {
        try
        {
            if (!File.Exists(_file))
            {
                return new MemoryData();
            }

            string json = File.ReadAllText(_file);

            return JsonSerializer.Deserialize<MemoryData>(json)
                   ?? new MemoryData();
        }
        catch
        {
            return new MemoryData();
        }
    }

    public void Save(MemoryData data)
    {
        string json =
            JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(_file, json);
    }
}