using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MonHub;

/// <summary>
/// Runs on every launcher start: creates the folders and points both emulators and der Randomizer at them.
/// Only touches the settings that belong to the MonHub layout – everything else the player changes stays.
/// </summary>
public static class HubSetup
{
    /// <summary>A step was skipped because its emulator was open – done again once it is closed (see <see cref="EnsurePending"/>).</summary>
    static bool _pending;

    static readonly object Gate = new();
    static int _catchingUp;

    /// <summary>
    /// Called when MonHub gets the focus back or files change: catches up on skipped emulator settings – in the
    /// background, so switching back to MonHub while an emulator is open never makes the window stutter.
    /// </summary>
    public static void EnsurePending()
    {
        if (!_pending || Interlocked.Exchange(ref _catchingUp, 1) == 1) return;
        Task.Run(() =>
        {
            try { EnsureAll(); }
            finally { Interlocked.Exchange(ref _catchingUp, 0); }
        });
    }

    /// <summary>Folders and emulator settings. One at a time (background catch-up and "Übernehmen" may meet).</summary>
    public static void EnsureAll()
    {
        lock (Gate) EnsureAllLocked();
    }

    static void EnsureAllLocked()
    {
        _pending = false;
        foreach (var dir in new[] { HubPaths.Roms, HubPaths.Randomized, HubPaths.MelonDSSaves, Path.Combine(HubPaths.MelonDSSaves, "Savestates"), HubPaths.DeSmuMESaves,
                                    HubPaths.MGBASaves, Path.Combine(HubPaths.MGBASaves, "Savestates"), HubPaths.AzaharSaves, HubPaths.Fangames, HubPaths.SettingsDir })
            Directory.CreateDirectory(dir);
        TryRun(EnsureMelonDS);
        TryRun(EnsureDeSmuME);
        TryRun(EnsureMGBA);
        TryRun(EnsureAzahar);
        TryRun(EnsureRandomizerConfig);
    }

    static void TryRun(Action step)
    {
        try { step(); }
        catch (Exception ex) { Debug.WriteLine(ex); } // a broken emulator config must never stop the launcher
    }

    /// <summary>melonDS: saves into Spielstände\melonDS, ROM dialog starts in ROMs, cheats on (for the Rare Candy cheat).</summary>
    static void EnsureMelonDS()
    {
        var toml = Path.Combine(HubPaths.MelonDS, "melonDS.toml");
        if (!File.Exists(toml)) return;
        if (IsRunning("melonDS"))
        {
            _pending = true; // melonDS rewrites its config on exit
            return;
        }
        var lines = File.ReadAllLines(toml).ToList();
        SetToml(lines, null, "LastROMFolder", Quote(HubPaths.Roms));
        SetToml(lines, "Instance0", "SaveFilePath", Quote(HubPaths.MelonDSSaves));
        SetToml(lines, "Instance0", "EnableCheats", "true");
        SetToml(lines, "Instance0", "SavestatePath", Quote(Path.Combine(HubPaths.MelonDSSaves, "Savestates")));
        ApplyBios(lines);
        HubConfig.Current.Controls?.ApplyToMelonDS(lines);
        if (string.IsNullOrEmpty(GetToml(lines, "Instance0.Window0", "Geometry")))
            SetToml(lines, "Instance0.Window0", "Geometry", "\"" + MaximizedQtGeometry() + "\"");
        SafeFile.WriteAllLines(toml, lines);
    }

    /// <summary>After "BIOS einspielen" / "Entfernen": melonDS uses the player's own BIOS, or its built-in one again.
    /// False while melonDS runs (it would write its old settings back when closing).</summary>
    public static bool ApplyBiosToMelonDS()
    {
        var toml = Path.Combine(HubPaths.MelonDS, "melonDS.toml");
        if (!File.Exists(toml) || IsRunning("melonDS")) return false;
        var lines = File.ReadAllLines(toml).ToList();
        ApplyBios(lines, force: true);
        SafeFile.WriteAllLines(toml, lines);
        return true;
    }

    /// <summary>Own BIOS files present: melonDS boots with them. Without, the player's melonDS settings stay as they are.</summary>
    static void ApplyBios(List<string> lines, bool force = false)
    {
        bool own = Bios.Installed;
        if (!own && !force) return;
        SetToml(lines, "DS", "BIOS7Path", own ? Quote(Bios.PathOf("bios7.bin")) : "\"\"");
        SetToml(lines, "DS", "BIOS9Path", own ? Quote(Bios.PathOf("bios9.bin")) : "\"\"");
        SetToml(lines, "DS", "FirmwarePath", own ? Quote(Bios.PathOf("firmware.bin")) : "\"\"");
        SetToml(lines, "Emu", "ExternalBIOSEnable", own ? "true" : "false");
    }

    /// <summary>
    /// Without a saved window size melonDS opens at 1× – a tiny picture. For the first start it gets a Qt
    /// window geometry (QWidget::saveGeometry format 3.0) that opens maximized on the main screen;
    /// afterwards melonDS saves the player's own size and this is never touched again.
    /// </summary>
    static string MaximizedQtGeometry()
    {
        var area = System.Windows.SystemParameters.WorkArea; // WPF units = Qt's logical pixels
        int left = (int)area.Left, top = (int)area.Top, width = (int)area.Width, height = (int)area.Height;
        // size when un-maximized: both DS screens side by side at 2×, centered
        int normalW = Math.Min(1040, width - 80), normalH = Math.Min(460, height - 80);
        int normalX = left + (width - normalW) / 2, normalY = top + (height - normalH) / 2;

        using var data = new MemoryStream();
        void Int(int value) { data.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]); } // QDataStream: big endian
        void Rect(int x, int y, int w, int h) { Int(x); Int(y); Int(x + w - 1); Int(y + h - 1); }
        Int(0x01D9D0CB);                          // magic
        data.Write([0, 3, 0, 0]);                 // version 3.0
        Rect(left, top, width, height);           // frame geometry
        Rect(normalX, normalY, normalW, normalH); // normal geometry
        Int(0);                                   // screen number
        data.WriteByte(2);                        // maximized (Qt::WindowMaximized)
        data.WriteByte(0);                        // not full screen
        Int((int)System.Windows.SystemParameters.PrimaryScreenWidth); // Qt ignores the geometry if the screen width differs a lot
        Rect(normalX, normalY, normalW, normalH); // geometry
        return Convert.ToBase64String(data.ToArray());
    }

    /// <summary>DeSmuME: saves into Spielstände\DeSmuME, ROM dialog starts in ROMs.</summary>
    static void EnsureDeSmuME()
    {
        if (HubPaths.DeSmuMEExe == null) return;
        if (IsRunning("DeSmuME"))
        {
            _pending = true;
            return;
        }
        var ini = Path.Combine(HubPaths.DeSmuME, "desmume.ini");
        var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
        SetIni(lines, "PathSettings", "Roms", HubPaths.Roms);
        SetIni(lines, "PathSettings", "Battery", HubPaths.DeSmuMESaves);
        SetIni(lines, "PathSettings", "States", Path.Combine(HubPaths.DeSmuMESaves, "Savestates"));
        SafeFile.WriteAllLines(ini, lines);
    }

    /// <summary>
    /// mGBA (portable: its config.ini sits next to the exe): saves into Spielstände\mGBA, the ROM dialog starts in ROMs.
    /// GBA/GB saves imported before mGBA came along (Spielstände\GBA und GB) are copied over once, so it finds them.
    /// </summary>
    static void EnsureMGBA()
    {
        if (HubPaths.MGBAExe == null) return;
        if (IsRunning("mGBA"))
        {
            _pending = true; // mGBA writes its config back on exit
            return;
        }
        var portable = Path.Combine(HubPaths.MGBA, "portable.ini");
        if (!File.Exists(portable)) File.WriteAllText(portable, "");
        var ini = Path.Combine(HubPaths.MGBA, "config.ini");
        var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
        SetIni(lines, "ports.qt", "savegamePath", HubPaths.MGBASaves);
        SetIni(lines, "ports.qt", "savestatePath", Path.Combine(HubPaths.MGBASaves, "Savestates"));
        SetIni(lines, "ports.qt", "lastDirectory", HubPaths.Roms);
        HubConfig.Current.Controls?.ApplyToMGBAConfig(lines);
        SafeFile.WriteAllLines(ini, lines);
        if (HubConfig.Current.Controls is { } controls)
        {
            var qt = Path.Combine(HubPaths.MGBA, "qt.ini");
            var qtLines = File.Exists(qt) ? File.ReadAllLines(qt).ToList() : new List<string>();
            controls.ApplyToMGBAShortcuts(qtLines);
            SafeFile.WriteAllLines(qt, qtLines);
        }

        if (Directory.Exists(HubPaths.GbaSaves))
            foreach (var save in Directory.GetFiles(HubPaths.GbaSaves, "*.sav"))
            {
                var target = Path.Combine(HubPaths.MGBASaves, Path.GetFileName(save));
                if (!File.Exists(target)) File.Copy(save, target);
            }
    }

    /// <summary>Azahar (3DS): portable, no update check, ROM dialog in ROMs, MonHub's keys. Azahar rewrites its config on exit.</summary>
    static void EnsureAzahar()
    {
        if (HubPaths.AzaharExe == null) return;
        if (IsRunning("azahar"))
        {
            _pending = true;
            return;
        }
        Azahar3ds.Ensure();
    }

    /// <summary>The preset picked on a first start (and when the chosen one is gone); afterwards the last one used is kept.</summary>
    public const string DefaultPreset = "Randomizer_Nuzlock_with_Items.rnqs";

    /// <summary>The default preset of this install, or any shipped one if it is missing.</summary>
    public static string? DefaultPresetPath()
    {
        var settingsDir = Path.Combine(HubPaths.AppDir, "Settings");
        if (!Directory.Exists(settingsDir)) return null;
        var preferred = Path.Combine(settingsDir, DefaultPreset);
        return File.Exists(preferred) ? preferred : Directory.GetFiles(settingsDir, "*.rnqs").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>First start: a ready-made randomizer config (ROMs, output, melonDS). Later changes by the player are kept.</summary>
    static void EnsureRandomizerConfig()
    {
        if (!File.Exists(HubPaths.RandomizerConfig)) RandoApp.AppConfig.Defaults().Save();
    }

    /// <summary>"Als Standard" in Emulatoren: the emulator der Randomizer starts new runs in. Its other settings stay as they are.</summary>
    public static void SetMainEmulator(string emulator)
    {
        var path = HubPaths.RandomizerConfig;
        var config = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject();
        config["EmulatorFolder"] = emulator == GameLibrary.DeSmuME ? HubPaths.DeSmuME : HubPaths.MelonDS;
        Directory.CreateDirectory(HubPaths.SettingsDir);
        SafeFile.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    static (DateTime At, string[] Names) _processes = (DateTime.MinValue, []);

    /// <summary>
    /// Whether a program whose name starts like that is running. The process list is taken once and reused for a second:
    /// listing all processes takes a while, and one catch-up asks for four emulators in a row.
    /// </summary>
    public static bool IsRunning(string processPrefix) =>
        ProcessNames().Any(name => name.StartsWith(processPrefix, StringComparison.OrdinalIgnoreCase));

    static string[] ProcessNames()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _processes.At < TimeSpan.FromSeconds(1)) return _processes.Names;
            var names = new List<string>();
            foreach (var process in Process.GetProcesses())
            {
                try { names.Add(process.ProcessName); }
                catch { /* gone meanwhile */ }
                finally { process.Dispose(); }
            }
            _processes = (DateTime.UtcNow, names.ToArray());
            return _processes.Names;
        }
    }

    static string Quote(string path) => "\"" + path.Replace('\\', '/') + "\"";

    /// <summary>Value of "key = value" in a TOML section without quotes, or null.</summary>
    static string? GetToml(List<string> lines, string section, string key)
    {
        int start = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (start < 0) return null;
        for (int i = start + 1; i < lines.Count && !lines[i].TrimStart().StartsWith('['); i++)
        {
            var parts = lines[i].Split('=', 2);
            if (parts.Length == 2 && parts[0].Trim() == key) return parts[1].Trim().Trim('"');
        }
        return null;
    }

    /// <summary>Sets "key = value" in a TOML section (null = top level), adding the key/section if missing.</summary>
    internal static void SetToml(List<string> lines, string? section, string key, string value)
    {
        int start = 0, end = lines.Count;
        if (section != null)
        {
            start = lines.FindIndex(l => l.Trim() == $"[{section}]");
            if (start < 0) { lines.Add(""); lines.Add($"[{section}]"); lines.Add($"{key} = {value}"); return; }
            start++;
        }
        for (int i = start; i < lines.Count; i++)
            if (lines[i].TrimStart().StartsWith('[')) { end = i; break; }
        for (int i = start; i < end; i++)
            if (lines[i].TrimStart().StartsWith(key + " ", StringComparison.Ordinal) || lines[i].TrimStart().StartsWith(key + "=", StringComparison.Ordinal))
            {
                lines[i] = $"{key} = {value}";
                return;
            }
        lines.Insert(start, $"{key} = {value}");
    }

    /// <summary>Sets key=value in an INI section, adding the key/section if missing.</summary>
    /// <summary>Removes key= from an INI section, if it is there.</summary>
    internal static void RemoveIni(List<string> lines, string section, string key)
    {
        int start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return;
        for (int i = start + 1; i < lines.Count && !lines[i].TrimStart().StartsWith('['); i++)
            if (lines[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
            {
                lines.RemoveAt(i);
                return;
            }
    }

    internal static void SetIni(List<string> lines, string section, string key, string value)
    {
        int start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) { lines.Add($"[{section}]"); lines.Add($"{key}={value}"); return; }
        int end = lines.Count;
        for (int i = start + 1; i < lines.Count; i++)
            if (lines[i].TrimStart().StartsWith('[')) { end = i; break; }
        for (int i = start + 1; i < end; i++)
            if (lines[i].StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { lines[i] = $"{key}={value}"; return; }
        lines.Insert(start + 1, $"{key}={value}");
    }
}
