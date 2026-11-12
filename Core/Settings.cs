// ============================================================================
//  ShakeLens - user settings (%APPDATA%\ShakeLens\settings.json)
// ============================================================================

using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Text.Json.Serialization;

namespace ShakeLens.Core;

public sealed class Settings
{
    public bool ShakeEnabled { get; set; } = true;
    public int Sensitivity { get; set; } = 1;          // 0 low, 1 medium, 2 high
    public int UploadSize { get; set; } = 1400;       // longest edge sent to Lens
    public string LensLanguage { get; set; } = "auto";
    public string Browser { get; set; } = "default";  // default | chrome | edge | firefox
    public string AiProvider { get; set; } = "none";  // none | gemini | openai | claude
    public string AiKey { get; set; } = "";
    public string AiModel { get; set; } = "gemini-2.5-flash";
    public string AiPrompt { get; set; } =
        "Look at this screenshot. Describe what it shows, transcribe any text you can read, " +
        "and answer any question visible in the image. Be brief.";
    public bool AutoStart { get; set; }

    [JsonIgnore]
    public ShakeDetector.Sensitivity Level => (ShakeDetector.Sensitivity)Math.Clamp(Sensitivity, 0, 2);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShakeLens");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options);
                if (settings is not null) return settings;
            }
        }
        catch { /* fall back to the defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { /* never crash because of the config */ }
    }

    // ------------------------------------------------------------ autostart --

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("ShakeLens") is string;
        }
        catch { return false; }
    }

    public void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (key is null) return;
            if (enabled)
            {
                string exe = Environment.ProcessPath ?? "ShakeLens.exe";
                key.SetValue("ShakeLens", $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue("ShakeLens", false);
            }
        }
        catch { /* ignore */ }
    }
}
