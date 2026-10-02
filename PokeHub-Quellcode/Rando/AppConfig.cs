using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RandoApp;

/// <summary>Everything the app remembers between starts. Saved as JSON next to the exe.</summary>
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
    /// <summary>Look of the app: see Themes.All (e.g. "pokeball", "gardevoir", "custom").</summary>
    public string Theme { get; set; } = "pokeball";
    /// <summary>Which ROM types show up in the dropdown (ids from RomInfo.FileTypes); empty = all.</summary>
    public List<string> RomFileTypes { get; set; } = new();
    /// <summary>Picture for the "custom" theme.</summary>
    public string CustomBackground { get; set; } = "";
    /// <summary>Own picture per theme id (e.g. a Gardevoir artwork); replaces the placeholder pixel sprite.</summary>
    public Dictionary<string, string> ThemeImages { get; set; } = new();
    public string LastRom { get; set; } = "";
    /// <summary>Emulator folder (DeSmuME or melonDS, detected by exe) – cheat files, save-name collision checks and "Spielen".</summary>
    public string EmulatorFolder { get; set; } = "";
    public string RandomizerJar { get; set; } = "";
    public string JavaPath { get; set; } = "java";
    public int JavaMaxMemoryMb { get; set; } = 4608;
    /// <summary>User-provided Action Replay codes per game code (e.g. "IRED"), overriding the built-in ones.</summary>
    public Dictionary<string, string> CustomCheats { get; set; } = new();

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>True when there was no config yet (first start on this PC).</summary>
    [JsonIgnore] public bool IsNew { get; private set; }

    /// <summary>%APPDATA%\PokeRando – the install folder may not be writable (e.g. Program Files).</summary>
    public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokeRando");
    /// <summary>Set via "--config &lt;file&gt;" (used by the MonHub launcher): the app then uses that file instead of %APPDATA%.</summary>
    public static string? ConfigOverride { get; set; }
    public static string ConfigPath => ConfigOverride ?? Path.Combine(DataDir, "randoapp.json");
    /// <summary>Older versions kept the config next to the exe.</summary>
    static string LegacyConfigPath => Path.Combine(AppContext.BaseDirectory, "randoapp.json");

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
            if (ConfigOverride == null && !File.Exists(ConfigPath) && File.Exists(LegacyConfigPath))
            {
                Directory.CreateDirectory(DataDir);
                File.Copy(LegacyConfigPath, ConfigPath);
            }
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), JsonOptions);
                if (cfg != null)
                {
                    cfg.CustomCheats ??= new();
                    cfg.ThemeImages ??= new();
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
        return CreateDefaults();
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        // atomic: MonHub and der Randomizer share this file, and a crash mid-write must not empty it
        SafeFile.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>First start: guess sensible paths from where the app lives (next to PokeRandoZX.jar).</summary>
    static AppConfig CreateDefaults()
    {
        var cfg = new AppConfig { IsNew = true };
        cfg.OutputFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PokeRando");
        var jar = FindJar();
        if (jar == null) return cfg;

        cfg.RandomizerJar = jar;
        var randoDir = Path.GetDirectoryName(jar)!;

        var settingsDir = Path.Combine(randoDir, "Settings");
        if (Directory.Exists(settingsDir))
            cfg.SettingsFile = Directory.GetFiles(settingsDir, "*.rnqs").OrderByDescending(File.GetLastWriteTime).FirstOrDefault() ?? "";

        var outDir = Path.Combine(randoDir, "RondamRom");
        if (Directory.Exists(outDir)) cfg.OutputFolder = outDir;

        var pokemonDir = Path.GetDirectoryName(randoDir);
        if (pokemonDir != null)
        {
            var romDir = Path.Combine(pokemonDir, "Rom", "Basegame");
            if (Directory.Exists(romDir)) cfg.RomFolder = romDir;
            try
            {
                cfg.EmulatorFolder = FindMostRecentDeSmuME(pokemonDir) ?? "";
            }
            catch (Exception)
            {
                // e.g. no access to a neighbouring folder – the emulator can still be picked in the settings.
            }
        }
        return cfg;
    }

    /// <summary>Of all DeSmuME installs next to the randomizer, the one whose Battery folder was written last (= the one actually played).</summary>
    static string? FindMostRecentDeSmuME(string pokemonDir) =>
        Directory.GetDirectories(pokemonDir)
            .Where(d => Directory.GetFiles(d, "DeSmuME*.exe").Length > 0)
            .OrderByDescending(d =>
            {
                var battery = Path.Combine(d, "Battery");
                return Directory.Exists(battery)
                    ? Directory.GetFiles(battery, "*.dsv").Select(File.GetLastWriteTime).DefaultIfEmpty(DateTime.MinValue).Max()
                    : DateTime.MinValue;
            })
            .FirstOrDefault();

    static string? FindJar()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PokeRandoZX.jar");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
