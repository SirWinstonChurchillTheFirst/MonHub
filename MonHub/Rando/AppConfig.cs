using System.IO;
using System.Text.Json;
using MonHub;

namespace RandoApp;

/// <summary>The randomizer's settings in MonHub ("Neuer Run"): System\Einstellungen\Randomizer.json.</summary>
public class AppConfig
{
    public string SettingsFile { get; set; } = "";
    public string RomFolder { get; set; } = "";
    public string OutputFolder { get; set; } = "";
    /// <summary>true = ask for a file name after randomizing, false = generate a unique one.</summary>
    public bool AskForName { get; set; }
    public bool RareCandyCheat { get; set; } = true;
    public bool CreateLog { get; set; }
    /// <summary>Also save the starter/static summary as "&lt;rom&gt;.spoiler.txt" (it's always shown in the app).</summary>
    public bool CreateSpoilerFile { get; set; } = true;
    public string LastRom { get; set; } = "";
    /// <summary>Emulator folder (DeSmuME or melonDS, detected by exe) – cheat files, save-name collision checks and "Spielen".</summary>
    public string EmulatorFolder { get; set; } = "";
    public string RandomizerJar { get; set; } = "";
    public string JavaPath { get; set; } = "java";
    public int JavaMaxMemoryMb { get; set; } = 4608;
    /// <summary>User-provided Action Replay codes per game code (e.g. "IRED"), overriding the built-in ones.</summary>
    public Dictionary<string, string> CustomCheats { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string ConfigPath => HubPaths.RandomizerConfig;

    /// <summary>The Java runtime shipped by the installer (jre\bin\java.exe), else whatever is configured / on PATH.</summary>
    public string ResolveJava()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "jre", "bin", "java.exe");
        if ((string.IsNullOrWhiteSpace(JavaPath) || JavaPath == "java") && File.Exists(bundled)) return bundled;
        return string.IsNullOrWhiteSpace(JavaPath) ? "java" : JavaPath;
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (cfg != null)
                {
                    cfg.CustomCheats ??= new();
                    // the randomizer moved (2.7: shipped as randomizer.jar) – an old path points to the shipped one again
                    if (!File.Exists(cfg.RandomizerJar) && File.Exists(HubPaths.RandomizerJar)) cfg.RandomizerJar = HubPaths.RandomizerJar;
                    return cfg;
                }
            }
        }
        catch
        {
            // Broken config -> start with defaults instead of crashing, but keep the broken file: the next Save would
            // overwrite it, and with it the player's paths and cheats.
            try { if (File.Exists(ConfigPath)) File.Copy(ConfigPath, ConfigPath + ".kaputt", overwrite: true); } catch { }
        }
        return Defaults();
    }

    /// <summary>A ready-made config for this install: its folders, its randomizer, the default preset, melonDS.</summary>
    public static AppConfig Defaults() => new()
    {
        SettingsFile = HubSetup.DefaultPresetPath() ?? "",
        RomFolder = HubPaths.Roms,
        OutputFolder = HubPaths.Randomized,
        EmulatorFolder = HubPaths.MelonDSExe != null ? HubPaths.MelonDS : HubPaths.DeSmuME,
        RandomizerJar = HubPaths.RandomizerJar,
    };

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        // atomic: a crash mid-write must not empty it
        SafeFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
