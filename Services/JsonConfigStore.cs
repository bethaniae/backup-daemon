using System.Text.Json;
using BackupManager.Models;

namespace BackupManager.Services;

public interface IConfigStore
{
    AppConfig Config { get; }
    void Load();
    void Save();
}

public class JsonConfigStore : IConfigStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public AppConfig Config { get; private set; } = new();

    public JsonConfigStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackupManager");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "config.json");
    }

    public void Load()
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, _options);
                if (cfg is not null)
                    Config = cfg;
            }
            catch
            {
                // Keep default config if file is corrupt.
            }
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, _options);
        File.WriteAllText(_filePath, json);
    }
}
