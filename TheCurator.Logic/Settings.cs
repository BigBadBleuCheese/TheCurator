namespace TheCurator.Logic;

public class Settings
{
    public string? AudioVoiceName { get; }

    static Settings LoadInstance()
    {
        var appDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        var settingsFileInfo = new FileInfo(Path.Combine(appDirectoryInfo.FullName, "settings.json"));
        if (settingsFileInfo.Exists)
            return JsonConvert.DeserializeObject<Settings>(File.ReadAllText(settingsFileInfo.FullName)) ?? new Settings();
        return new Settings();
    }

    public static Settings Instance { get; } = LoadInstance();
}
