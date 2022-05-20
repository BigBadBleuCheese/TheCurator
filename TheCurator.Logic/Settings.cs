namespace TheCurator.Logic;

public class Settings
{
    public string? AudioVoiceName { get; set; }
    public string[]? AudioPSAs { get; set; }
    public int? AudioPSAFrequency { get; set; }

    static Settings LoadInstance()
    {
        var appDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        var settingsFileInfo = new FileInfo(Path.Combine(appDirectoryInfo.FullName, "settings.json"));
        if (settingsFileInfo.Exists)
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsFileInfo.FullName), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Settings();
        return new Settings();
    }

    public static Settings Instance { get; } = LoadInstance();
}
