using System.IO;
using System.Text.Json;

namespace MyKey.Desktop;

public sealed class AppSettingsStore
{
    private readonly string _settingsPath;

    public AppSettingsStore()
    {
        var dataDir = AppPaths.DataDirectory;
        Directory.CreateDirectory(dataDir);
        _settingsPath = Path.Combine(dataDir, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var temporary = _settingsPath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, _settingsPath, true);
    }
}

public sealed class AppSettings
{
    public bool CloseToTray { get; set; }
    public string Theme { get; set; } = "light";
    public bool CheckUpdatesOnStartup { get; set; } = true;
}
