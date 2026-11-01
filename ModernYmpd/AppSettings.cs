using System.Text.Json;
using System.Text.Json.Serialization;

namespace ModernYmpd;

public sealed class AppSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 6600;
    [JsonIgnore] public string Password { get; set; } = "";
    public bool AutoFillEnabled { get; set; }
    public int FillTarget { get; set; } = 20;
    public bool ShowFilePath { get; set; }
    public bool AskPauseOnExit { get; set; }

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "modern-ympd", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (settings is not null)
                {
                    settings.Port = Math.Clamp(settings.Port, 1, 65535);
                    settings.FillTarget = Math.Clamp(settings.FillTarget, 1, 500);
                    if (string.IsNullOrWhiteSpace(settings.Host)) settings.Host = "127.0.0.1";
                    return settings;
                }
            }
        }
        catch (Exception) { /* Keep the app usable if the settings file is damaged. */ }
        return new AppSettings();
    }

    public void Save()
    {
        var path = FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, path, true);
    }
}
