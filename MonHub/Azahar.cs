using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MonHub;

/// <summary>What the header of a 3DS game (.3ds/.cci cartridge image, .cxi from the randomizer) says.</summary>
public record Rom3ds(string ProductCode, ulong TitleId, bool Encrypted)
{
    /// <summary>"EKJA" from "CTR-P-EKJA" – used like a DS game code (first 3 letters = game).</summary>
    public string GameCode => ProductCode.Length >= 10 && ProductCode.StartsWith("CTR-", StringComparison.Ordinal) ? ProductCode[6..10] : "";

    public static bool IsFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".3ds" or ".cci" or ".cxi";

    static readonly Dictionary<(string Path, DateTime Time), Rom3ds?> Cache = new();

    /// <summary>The NCCH header of the game (first partition of a cartridge image), null if it isn't one.</summary>
    public static Rom3ds? Read(string path)
    {
        DateTime time;
        try { time = File.GetLastWriteTimeUtc(path); }
        catch { return null; }
        lock (Cache)
            if (Cache.TryGetValue((path, time), out var known)) return known;
        Rom3ds? result = null;
        try
        {
            using var fs = File.OpenRead(path);
            var head = new byte[0x200];
            fs.ReadExactly(head);
            long ncch = 0;
            if (head.AsSpan(0x100, 4).SequenceEqual("NCSD"u8))
            {
                ncch = (long)BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(0x120)) * 0x200; // partition 0 = the game
                fs.Position = ncch;
                fs.ReadExactly(head);
            }
            if (head.AsSpan(0x100, 4).SequenceEqual("NCCH"u8))
                result = new Rom3ds(
                    Encoding.ASCII.GetString(head, 0x150, 16).TrimEnd('\0', ' '),
                    BinaryPrimitives.ReadUInt64LittleEndian(head.AsSpan(0x118)),
                    (head[0x18F] & 0x04) == 0); // flag "NoCrypto" missing = still encrypted
        }
        catch
        {
            result = null; // unreadable: listed without details
        }
        lock (Cache)
            Cache[(path, time)] = result;
        return result;
    }
}

/// <summary>
/// Azahar (3DS emulator, portable: its "user" folder sits next to azahar.exe).
/// Every game gets its own SD card folder in Spielstände\Azahar\&lt;game&gt; – runs of the same game share the game's
/// title ID, so without that they would all load one save. MonHub points Azahar at the right folder before each start.
/// </summary>
public static class Azahar3ds
{
    const string Zeros = "00000000000000000000000000000000";

    public static string UserDir => Path.Combine(HubPaths.Azahar, "user");
    static string ConfigFile => Path.Combine(UserDir, "config", "qt-config.ini");

    /// <summary>
    /// Longest path the games create below an SD card folder ("\Nintendo 3DS\{32}\{32}\extdata\00000000\0000055D\user\…").
    /// Azahar can't go past Windows' 260 characters – a game whose extra data can't be created hangs on "The game is
    /// preparing to load", so the card folder must leave room for it.
    /// </summary>
    const int SdCardDepth = 140;
    const int MaxPath = 259;

    /// <summary>
    /// The virtual SD card of one game: Spielstände\Azahar\&lt;game&gt;. When that would be too long (long user name, long
    /// hack name + date), the name is shortened with a short code that keeps it unique; a card that exists already stays.
    /// </summary>
    public static string SdCardOf(string gameName)
    {
        var full = Path.Combine(HubPaths.AzaharSaves, gameName);
        int room = MaxPath - SdCardDepth - HubPaths.AzaharSaves.Length - 1;
        if (full.Length + SdCardDepth <= MaxPath || Directory.Exists(full) || room < 10) return full;
        var code = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(gameName.ToLowerInvariant())))[..6];
        return Path.Combine(HubPaths.AzaharSaves, gameName[..Math.Min(gameName.Length, room - 7)].TrimEnd(' ', '.', '_', '-') + "~" + code);
    }

    /// <summary>The MonHub folder lies so deep that even a short card name leaves no room for the games' data.</summary>
    static bool TooDeep => HubPaths.AzaharSaves.Length + 1 + 10 + SdCardDepth > MaxPath;

    /// <summary>Where the Pokémon games keep their save ("main") on that SD card.</summary>
    public static string SavePath(string gameName, ulong titleId) => Path.Combine(SdCardOf(gameName), "Nintendo 3DS", Zeros, Zeros,
        "title", (titleId >> 32).ToString("x8"), (titleId & 0xFFFFFFFF).ToString("x8"), "data", "00000001", "main");

    /// <summary>A run's cheats (Rare Candy) for Azahar, next to the ROM – copied into Azahar's cheat file when it starts.</summary>
    public static string CheatFileOf(string rom) => Path.ChangeExtension(rom, ".azahar-cheats.txt");

    /// <summary>First line of every cheat file MonHub writes – only those are replaced or removed.</summary>
    const string Marker = "[Rare Candy x999 (MonHub)]";

    /// <summary>Rare Candy x999 in the first slot of the medicine pocket (addresses from the CTRPF/Gateway cheat collection).</summary>
    public static string? RareCandyCode(string gameCode) => gameCode.Length < 3 ? null : gameCode[..3].ToUpperInvariant() switch
    {
        "EKJ" or "EK2" => "08C67EBC 03E70032",                                     // X/Y: item 50, count 999
        "ECR" or "ECL" => "08C6F5E0 03E70032",                                     // Omega Ruby / Alpha Sapphire
        "BND" or "BNE" => "D3000000 330D647C\n00000000 000F9C32\nD2000000 00000000", // Sun/Moon: 10-bit item | 10-bit count
        "A2A" or "A2B" => "D3000000 330124A8\n00000000 000F9C32\nD2000000 00000000", // Ultra Sun/Ultra Moon
        _ => null,
    };

    public static string CheatText(string code) => $"{Marker}\n*citra_enabled\n{code}\n";

    static string FpsName => Txt.L("[60 FPS, R halten = 30 FPS (MonHub)]", "[60 FPS, hold R = 30 FPS (MonHub)]");

    /// <summary>
    /// 60 FPS (codes by Reshiban, 60FPS-AR-CHEATS-3DS; v1.0 addresses). These games tie their speed to the frame rate, so
    /// everything runs twice as fast; holding R gives the normal 30 FPS. Only written while the game's own 30-FPS value (1)
    /// is there – on a hack or another version with a different layout the code does nothing.
    /// </summary>
    public static string? SixtyFpsCode(string gameCode) => gameCode.Length < 3 ? null : gameCode[..3].ToUpperInvariant() switch
    {
        "EKJ" or "EK2" => Fps(0x08000000, 0x0C61A85), // X/Y
        "ECR" or "ECL" => Fps(0x08000000, 0x0C650C1), // Omega Ruby / Alpha Sapphire
        "BND" or "BNE" => Fps(0x30000000, 0x417E569), // Sun/Moon
        "A2A" or "A2B" => Fps(0x30000000, 0x3F67FE9), // Ultra Sun/Ultra Moon
        _ => null,
    };

    /// <summary>The frame byte sits at an odd address: compared as the high byte of the halfword before it.</summary>
    static string Fps(uint offset, uint address) => string.Join('\n',
        $"D3000000 {offset:X8}",
        $"9{address - 1:X7} 00FF0100", // if the byte is 1 (30 FPS) …
        $"2{address:X7} 00000000",     // … make it 0 (60 FPS)
        "D0000000 00000000",
        "DD000000 00000100",           // R held: 30 FPS
        $"2{address:X7} 00000001",
        "D2000000 00000000");

    /// <summary>A cheat file MonHub wrote (every entry it writes ends with "(MonHub)]").</summary>
    static bool IsOurs(string file) => File.ReadLines(file).FirstOrDefault() is { } first
        && first.EndsWith("(MonHub)]", StringComparison.Ordinal);

    /// <summary>
    /// Before a game starts: its own SD card folder, and its cheats (or none). Returns an error text, or null.
    /// </summary>
    public static string? Prepare(string rom)
    {
        var info = Rom3ds.Read(rom);
        if (info == null) return Txt.L("Das ist kein 3DS-Spiel, das Azahar starten kann.", "This isn't a 3DS game Azahar can start.");
        if (info.Encrypted)
            return Txt.L("Dieses 3DS-Spiel ist noch verschlüsselt – Azahar startet nur entschlüsselte Spiele (.3ds/.cci/.cxi).",
                "This 3DS game is still encrypted – Azahar only starts decrypted games (.3ds/.cci/.cxi).");

        if (TooDeep)
            return Txt.L($"Der MonHub-Ordner liegt zu tief für 3DS-Spiele:\n{HubPaths.Root}\n\nDie 3DS-Spiele legen sehr verschachtelte Ordner an, und Windows erlaubt nur 260 Zeichen pro Pfad. " +
                         "Installier MonHub bitte in einen kürzeren Ordner, z. B. C:\\MonHub.",
                         $"The MonHub folder is too deep for 3DS games:\n{HubPaths.Root}\n\n3DS games create deeply nested folders, and Windows only allows 260 characters per path. " +
                         "Please install MonHub into a shorter folder, e.g. C:\\MonHub.");

        var name = Path.GetFileNameWithoutExtension(rom);
        Directory.CreateDirectory(SdCardOf(name));
        var lines = ReadConfig();
        SetStorage(lines, SdCardOf(name));
        // full speed every time: Azahar keeps a speed limit changed in the game (e.g. a "-" pressed by accident: 90 %
        // speed makes 30-FPS games judder)
        Set(lines, "Renderer", "frame_limit", "100");
        // a controller plugged in: MonHub's controller profile, else the keyboard one (Azahar takes one input per button)
        bool padProfile = lines.Any(l => l == $@"profiles\2\name={ControlSettings.ControllerProfile}");
        Set(lines, "Controls", "profile", padProfile && ControlSettings.AnyPadConnected() ? "1" : "0");
        WriteConfig(lines);

        var cheats = Path.Combine(UserDir, "cheats");
        var target = Path.Combine(cheats, $"{info.TitleId:X16}.txt");
        // this game's cheats: the run's Rare Candy and, if switched on, 60 FPS
        var parts = new List<string>();
        var own = CheatFileOf(rom);
        if (File.Exists(own)) parts.Add(File.ReadAllText(own).TrimEnd());
        if (HubConfig.Current.Azahar60Fps && SixtyFpsCode(info.GameCode) is { } fps) parts.Add($"{FpsName}\n*citra_enabled\n{fps}");
        if (parts.Count > 0)
        {
            Directory.CreateDirectory(cheats);
            File.WriteAllText(target, string.Join("\n\n", parts) + "\n", new UTF8Encoding(false));
        }
        else if (File.Exists(target) && IsOurs(target))
        {
            File.Delete(target); // another run's cheats – not for this game
        }
        return null;
    }

    /// <summary>Settings that belong to the MonHub layout (called by <see cref="HubSetup"/>, while Azahar is closed).</summary>
    public static void Ensure()
    {
        bool fresh = !File.Exists(ConfigFile);
        Directory.CreateDirectory(UserDir); // portable mode
        var lines = ReadConfig();
        Set(lines, "Miscellaneous", "check_for_update_on_start", "false"); // MonHub brings its own version
        Set(lines, "Paths", "romsPath", Quote(HubPaths.Roms));
        if (!lines.Any(l => l.StartsWith("sdmc_directory=", StringComparison.Ordinal)))
            SetStorage(lines, Path.Combine(UserDir, "sdmc"));
        // MonHub's controller profile always; its keyboard when changed under "Emulatoren", or on the very first start
        // (same keys as melonDS/mGBA)
        var controls = HubConfig.Current.Controls;
        (controls ?? new ControlSettings()).ApplyToAzahar(lines, keyboard: controls != null || fresh);
        // a sharp picture with both screens side by side – set once; what the player changes in Azahar afterwards stays
        if (HubConfig.Current.AzaharLook < 1)
        {
            Set(lines, "Layout", "layout_option", "3");                   // side by side (like the DS emulators)
            Set(lines, "Renderer", "resolution_factor", "3");             // 3× native (1200×720 per screen)
            Set(lines, "Renderer", "async_shader_compilation", "true");  // no stutter while new effects compile
            Set(lines, "Renderer", "use_disk_shader_cache", "true");
            HubConfig.Current.AzaharLook = 1;
            HubConfig.Current.Save();
        }
        // smoother play (measured 2026-10-01 in Pokémon X, 2 min walking and menus): Vulkan needs half the time per frame
        // of OpenGL (median 1.9 ms vs 4.3 ms, 99 % of frames under 6 ms vs 16 ms) – more room before a frame comes late on
        // slower PCs. Its first-time pipeline builds are kept in the shader cache. OpenGL stays on PCs without Vulkan.
        // The "+" / "-" speed keys go: pressed by accident they slow the game down for good.
        if (HubConfig.Current.AzaharLook < 2)
        {
            Set(lines, "Renderer", "graphics_api", VulkanAvailable() ? "2" : "1");
            Set(lines, "Renderer", "async_shader_compilation", "true");
            Set(lines, "Renderer", "use_disk_shader_cache", "true");
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Decrease%20Speed%20Limit\KeySeq", "");
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Increase%20Speed%20Limit\KeySeq", "");
            HubConfig.Current.AzaharLook = 2;
            HubConfig.Current.Save();
        }
        WriteConfig(lines);
    }

    /// <summary>
    /// Whether the PC can run Vulkan: the Vulkan loader is there and a graphics driver registered for it (AMD/Intel under
    /// Khronos\Vulkan\Drivers, NVIDIA/all drivers at the display adapter as VulkanDriverName).
    /// </summary>
    static bool VulkanAvailable()
    {
        try
        {
            if (!File.Exists(Path.Combine(Environment.SystemDirectory, "vulkan-1.dll"))) return false;
            using (var drivers = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\Vulkan\Drivers"))
                if (drivers?.ValueCount > 0) return true;
            using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (adapters == null) return false;
            foreach (var sub in adapters.GetSubKeyNames())
            {
                try
                {
                    using var adapter = adapters.OpenSubKey(sub);
                    if (adapter?.GetValue("VulkanDriverName") != null || adapter?.GetValue("VulkanDriverNameWow") != null) return true;
                }
                catch
                {
                    // a key without access: next one
                }
            }
        }
        catch
        {
            // no registry access: stay with OpenGL, which every PC can run
        }
        return false;
    }

    /// <summary>Azahar ignores folders that don't exist (and then saves its default back) – so both are created first.</summary>
    static void SetStorage(List<string> lines, string sdCard)
    {
        Directory.CreateDirectory(sdCard);
        Directory.CreateDirectory(Path.Combine(UserDir, "nand"));
        Set(lines, "Data%20Storage", "use_custom_storage", "true");
        Set(lines, "Data%20Storage", "nand_directory", Quote(Path.Combine(UserDir, "nand")));
        Set(lines, "Data%20Storage", "sdmc_directory", Quote(sdCard));
    }

    /// <summary>A setting plus its "\default=false" twin – without it Azahar would ignore the value.</summary>
    internal static void Set(List<string> lines, string section, string key, string value)
    {
        HubSetup.SetIni(lines, section, key + "\\default", "false");
        HubSetup.SetIni(lines, section, key, value);
    }

    /// <summary>Qt settings value: forward slashes, a trailing one for folders, quoted (commas would split it).</summary>
    static string Quote(string folder) => "\"" + folder.Replace('\\', '/').TrimEnd('/') + "/\"";

    static List<string> ReadConfig() => File.Exists(ConfigFile) ? File.ReadAllLines(ConfigFile).ToList() : [];

    static void WriteConfig(List<string> lines)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
        SafeFile.WriteAllLines(ConfigFile, lines);
    }
}
