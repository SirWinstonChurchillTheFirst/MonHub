using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MonHub;

public enum GameSystem { DS, GBA, GB, N3DS, Other }

/// <summary>A save of a game in one emulator's folder: melonDS keeps raw .sav files, DeSmuME .dsv files.</summary>
public record SaveFile(string Path, string Emulator, DateTime Time);

/// <summary>A game in MonHub (ROMs or Randomisierte ROMs) with its saves.</summary>
public class GameEntry
{
    public required string Rom { get; init; }
    public required bool IsRun { get; init; }
    public required GameSystem System { get; init; }
    public required DateTime RomTime { get; init; }
    public SaveFile? MelonDSSave { get; init; }
    public SaveFile? DeSmuMESave { get; init; }
    public SaveFile? MGBASave { get; init; }
    public SaveFile? AzaharSave { get; init; }
    /// <summary>3DS: the title ID (runs keep the one of their game).</summary>
    public ulong TitleId { get; init; }
    /// <summary>From the ROM header: "CPUD" = Platin (DE). Empty for Game Boy games and unreadable files.</summary>
    public string GameCode { get; init; } = "";
    public string HeaderTitle { get; init; } = "";

    public string Name => Path.GetFileNameWithoutExtension(Rom);
    public bool IsDS => System == GameSystem.DS;
    /// <summary>Game Boy, Game Boy Color, GBA – played in mGBA.</summary>
    public bool IsGameBoy => System is GameSystem.GB or GameSystem.GBA;
    public bool Is3DS => System == GameSystem.N3DS;

    /// <summary>DS ROM with an encrypted secure area (many US dumps): melonDS can't boot it without a Nintendo BIOS, DeSmuME can.</summary>
    public bool NeedsDeSmuME => _needsDeSmuME ??= IsDS && !Bios.Installed && RomCheck.HasEncryptedSecureArea(Rom);
    bool? _needsDeSmuME;

    /// <summary>The save that counts: the newest one. melonDS wins a tie – older imports put the same save into both folders.</summary>
    public SaveFile? NewestSave => new[] { MelonDSSave, DeSmuMESave, MGBASave, AzaharSave }.Where(s => s != null).MaxBy(s => s!.Time);

    public SaveFile? SaveOf(string emulator) => emulator switch
    {
        GameLibrary.MelonDS => MelonDSSave,
        GameLibrary.DeSmuME => DeSmuMESave,
        GameLibrary.MGBA => MGBASave,
        GameLibrary.Azahar => AzaharSave,
        _ => null,
    };

    public string KindText => IsRun ? "Run" : "Spiel";

    /// <summary>Which save layout the game has: by product code (GBA, DS, 3DS), Game Boy games by their header title.</summary>
    public SaveFamily? Family => SaveInspector.FamilyOf(GameCode) ?? (IsGameBoy && System == GameSystem.GB ? SaveInspector.FamilyOfTitle(HeaderTitle) : null);

    /// <summary>What the newest save says (trainer, play time, badges, team) – null for games and hacks it can't read and for empty saves. Read on first use.</summary>
    public SaveSummary? Summary
    {
        get
        {
            if (!_summaryRead)
            {
                _summaryRead = true;
                _summary = NewestSave != null && Family is { } family ? SaveInspector.Read(NewestSave.Path, family) : null;
            }
            return _summary;
        }
    }
    SaveSummary? _summary;
    bool _summaryRead;
    public string SystemText => System switch
    {
        GameSystem.DS => "DS",
        GameSystem.GBA => "GBA",
        GameSystem.GB => "GB/GBC",
        GameSystem.N3DS => "3DS",
        _ => Path.GetExtension(Rom).TrimStart('.').ToUpperInvariant(),
    };
    public string SaveText => NewestSave is { } save ? $"{TimeText.Ago(save.Time)} · {save.Emulator}" : "–";
}

/// <summary>
/// The games in MonHub and their saves in Spielstände – what "Weiterspielen" and "Spiel auswählen" work with.
/// Saves are matched to a ROM by name, like the emulators do: "Platin_run.nds" ↔ "Platin_run.sav" / "Platin_run.dsv".
/// </summary>
public static class GameLibrary
{
    public const string MelonDS = "melonDS";
    public const string DeSmuME = "DeSmuME";
    public const string MGBA = "mGBA";
    public const string Azahar = "Azahar";

    /// <summary>All games – the last saved first, then runs (newest first), then the rest by name.</summary>
    public static List<GameEntry> Load()
    {
        var melonDSSaves = SavesIn(HubPaths.MelonDSSaves, ".sav", MelonDS);
        // DeSmuME loads "<game>.dsv"; only when there is none it imports a raw "<game>.sav" from its folder
        var deSmuMESaves = SavesIn(HubPaths.DeSmuMESaves, ".sav", DeSmuME);
        foreach (var (name, dsv) in SavesIn(HubPaths.DeSmuMESaves, ".dsv", DeSmuME))
            deSmuMESaves[name] = dsv;
        var mgbaSaves = SavesIn(HubPaths.MGBASaves, ".sav", MGBA);
        var games = new List<GameEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // saves go by name: a run and a ROM of the same name share them
        foreach (var (dir, isRun) in new[] { (HubPaths.Randomized, true), (HubPaths.Roms, false) })
            foreach (var rom in RomFiles(dir))
            {
                var name = Path.GetFileNameWithoutExtension(rom);
                if (!names.Add(name)) continue;
                var system = SystemOf(rom);
                var (code, title) = Header(rom);
                var n3ds = system == GameSystem.N3DS ? Rom3ds.Read(rom) : null;
                games.Add(new GameEntry
                {
                    Rom = rom,
                    IsRun = isRun,
                    System = system,
                    RomTime = File.GetLastWriteTime(rom),
                    GameCode = n3ds?.GameCode ?? code,
                    HeaderTitle = title,
                    TitleId = n3ds?.TitleId ?? 0,
                    AzaharSave = n3ds != null ? Played(Written(AzaharSaveOf(name, n3ds.TitleId)), n3ds.GameCode) : null,
                    MelonDSSave = system == GameSystem.DS ? Written(melonDSSaves.GetValueOrDefault(name)) : null,
                    DeSmuMESave = system == GameSystem.DS ? Written(deSmuMESaves.GetValueOrDefault(name)) : null,
                    MGBASave = system is GameSystem.GB or GameSystem.GBA ? Written(mgbaSaves.GetValueOrDefault(name)) : null,
                });
            }
        return games
            .OrderByDescending(g => g.NewestSave?.Time ?? DateTime.MinValue)
            .ThenByDescending(g => g.IsRun ? g.RomTime : DateTime.MinValue)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// "Weiterspielen": the game with the newest save in Spielstände, in the emulator that holds that save.
    /// Nothing saved yet: nothing to continue (the trainer card and the continue box only ever show real saves).
    /// </summary>
    public static (GameEntry Game, string? Emulator, bool Saved)? Continue(List<GameEntry> games)
    {
        // only real saves count – a ROM that was just created is no reason to continue it
        var saved = games.Where(g => g.NewestSave != null).MaxBy(g => g.NewestSave!.Time);
        return saved == null ? null : (saved, saved.NewestSave!.Emulator, true);
    }

    /// <summary>The emulator der Randomizer uses (its "EmulatorFolder" setting): melonDS unless that's DeSmuME.</summary>
    /// <summary>Where a game without save starts: der Randomizer's emulator, DeSmuME for ROMs melonDS can't boot.</summary>
    public static string? EmulatorFor(GameEntry game) =>
        game.Is3DS ? (HubPaths.AzaharExe != null ? Azahar : null)
        : game.IsGameBoy ? (HubPaths.MGBAExe != null ? MGBA : null)
        : !game.IsDS ? null
        : game.NeedsDeSmuME && HubPaths.DeSmuMEExe != null ? DeSmuME : MainEmulator();

    public static string MainEmulator()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(HubPaths.RandomizerConfig));
            if (doc.RootElement.TryGetProperty("EmulatorFolder", out var value) && value.GetString() is { Length: > 0 } folder
                && Directory.Exists(folder) && Directory.GetFiles(folder, "DeSmuME*.exe").Length > 0 && HubPaths.DeSmuMEExe != null)
                return DeSmuME;
        }
        catch
        {
            // no or broken der Randomizer settings – default below
        }
        return HubPaths.MelonDSExe != null ? MelonDS : DeSmuME;
    }

    public static string? ExeFor(string? emulator) => emulator switch
    {
        MelonDS => HubPaths.MelonDSExe,
        DeSmuME => HubPaths.DeSmuMEExe,
        MGBA => HubPaths.MGBAExe,
        Azahar => HubPaths.AzaharExe,
        _ => null,
    };

    public static string SavePath(string emulator, string gameName) => emulator == MelonDS
        ? Path.Combine(HubPaths.MelonDSSaves, gameName + ".sav")
        : Path.Combine(HubPaths.DeSmuMESaves, gameName + ".dsv");

    /// <summary>Whether the game's newest save can be given to <paramref name="emulator"/> (DeSmuME only takes the 512 KB Pokémon saves).</summary>
    public static bool CanTransfer(GameEntry game, string emulator) =>
        game.NewestSave is { } save && (emulator == MelonDS || new FileInfo(save.Path).Length is SaveFormat.DsSaveSize or SaveFormat.DsvSaveSize);

    /// <summary>
    /// Gives <paramref name="emulator"/> the game's newest save (melonDS .sav ⇄ DeSmuME .dsv). A save it had before
    /// goes to the recycle bin. Returns an error text, or null when done.
    /// </summary>
    public static string? TransferSave(GameEntry game, string emulator)
    {
        var source = game.NewestSave;
        if (source == null || source.Emulator == emulator) return null;
        byte[] data;
        try { data = File.ReadAllBytes(source.Path); }
        catch (Exception ex) { return Txt.L("Spielstand nicht lesbar: ", "Save can't be read: ") + ex.Message; }
        var converted = emulator == MelonDS ? SaveFormat.DsvToRaw(data) : SaveFormat.RawToDsv(data);
        if (converted == null) return Txt.L($"Dieser Spielstand kann nicht nach {emulator} übernommen werden (unbekanntes Format).",
            $"This save can't be taken over to {emulator} (unknown format).");

        var target = SavePath(emulator, game.Name);
        if (File.Exists(target) && !RecycleBin.Delete(target))
            return Txt.L($"Der alte {emulator}-Spielstand konnte nicht in den Papierkorb gelegt werden – ist das Spiel noch offen?",
                $"The old {emulator} save couldn't be moved to the Recycle Bin – is the game still open?");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafeFile.WriteAllBytes(target, converted);
            // same save, one second "newer": this emulator is now the one to continue in
            File.SetLastWriteTime(target, source.Time.AddSeconds(1));
        }
        catch (Exception ex)
        {
            return Txt.L("Spielstand konnte nicht übernommen werden: ", "The save couldn't be taken over: ") + ex.Message;
        }
        return null;
    }

    public enum FileRole { Rom, Save, Savestate, Cheats, Spoiler, Tracker }

    /// <summary>
    /// Everything that belongs to a game: the ROM, its saves and savestates in both emulators, the cheat files
    /// (melonDS .mch, DeSmuME .dct) and der Randomizer's spoiler and log. Only files that exist.
    /// </summary>
    public static List<(string Path, FileRole Role)> FilesOf(GameEntry game)
    {
        var dir = Path.GetDirectoryName(game.Rom)!;
        var name = game.Name;
        var files = new List<(string Path, FileRole Role)>
        {
            (game.Rom, FileRole.Rom),
            (Path.Combine(HubPaths.MelonDSSaves, name + ".sav"), FileRole.Save),
            (Path.Combine(HubPaths.DeSmuMESaves, name + ".dsv"), FileRole.Save),
            (Path.Combine(HubPaths.DeSmuMESaves, name + ".sav"), FileRole.Save),  // DeSmuME also takes a raw .sav
            (Path.Combine(dir, name + ".sav"), FileRole.Save),                    // GBA emulators save next to the ROM
            (Path.Combine(HubPaths.GbaSaves, name + ".sav"), FileRole.Save),      // imported GBA/GB saves (before mGBA)
            (Path.Combine(HubPaths.MGBASaves, name + ".sav"), FileRole.Save),     // mGBA
            (Path.Combine(dir, name + ".cheats"), FileRole.Cheats),               // mGBA cheats next to the ROM
            (Path.Combine(dir, name + ".mch"), FileRole.Cheats),                  // melonDS keeps cheats next to the ROM …
            (Path.Combine(MelonDSCheatDir() ?? dir, name + ".mch"), FileRole.Cheats), // … unless a cheat folder is set
            (Path.Combine(DeSmuMECheatDir(), name + ".dct"), FileRole.Cheats),
            (Path.Combine(dir, name + ".spoiler.txt"), FileRole.Spoiler),         // der Randomizer
            (game.Rom + ".log", FileRole.Spoiler),
            (Path.Combine(HubPaths.Nuzlocke, name + ".json"), FileRole.Tracker),  // Nuzlocke tracker of this ROM
            (Azahar3ds.CheatFileOf(game.Rom), FileRole.Cheats),                     // Azahar: Rare Candy of a 3DS run
        };
        if (Directory.Exists(Azahar3ds.SdCardOf(name)))
            files.Add((Azahar3ds.SdCardOf(name), FileRole.Save));                   // Azahar: the game's own SD card (folder)
        files.AddRange(SafeFiles(Path.Combine(HubPaths.MelonDSSaves, "Savestates"), name + ".ml*").Select(f => (f, FileRole.Savestate)));
        files.AddRange(SafeFiles(Path.Combine(HubPaths.DeSmuMESaves, "Savestates"), name + ".ds*").Select(f => (f, FileRole.Savestate)));
        files.AddRange(SafeFiles(Path.Combine(HubPaths.MGBASaves, "Savestates"), name + ".ss*").Select(f => (f, FileRole.Savestate)));
        return files.Where(f => File.Exists(f.Path) || Directory.Exists(f.Path)).DistinctBy(f => f.Path.ToLowerInvariant()).ToList();
    }

    /// <summary>Moves every save of the game to the recycle bin: both emulators, savestates too. False if something stayed.</summary>
    public static bool DeleteSaves(GameEntry game) =>
        RecycleBin.Delete(FilesOf(game).Where(f => f.Role is FileRole.Save or FileRole.Savestate).Select(f => f.Path).ToArray());

    /// <summary>Moves the game and everything that belongs to it (see <see cref="FilesOf"/>) to the recycle bin.</summary>
    public static bool DeleteGame(GameEntry game) =>
        RecycleBin.Delete(FilesOf(game).Select(f => f.Path).ToArray());

    // ---------- saves without a game ----------

    static readonly Regex AnySave = new(@"^\.sav$", RegexOptions.IgnoreCase);
    static readonly Regex DeSmuMESave = new(@"^\.(dsv|sav)$", RegexOptions.IgnoreCase);
    static readonly Regex MelonDSState = new(@"^\.ml.$", RegexOptions.IgnoreCase);       // slots .ml1 … .ml8
    static readonly Regex DeSmuMEState = new(@"^\.ds[0-9t]$", RegexOptions.IgnoreCase);  // slots .ds0 … .ds9, "save as" .dst
    static readonly Regex MGBAState = new(@"^\.ss[0-9]$", RegexOptions.IgnoreCase);      // slots .ss1 … .ss9

    /// <summary>
    /// Saves no game can load anymore – nothing of that name in ROMs / Randomisierte ROMs, usually because the ROM was
    /// deleted or renamed in the Explorer, or a save was imported without its ROM. Newest first, one entry per game name.
    /// </summary>
    public static List<OrphanSave> OrphanSaves()
    {
        var known = KnownRomNames();
        (string Dir, bool Recursive, string Place, Regex Extension, bool Savestate)[] places =
        [
            (HubPaths.MelonDSSaves, false, MelonDS, AnySave, false),
            (Path.Combine(HubPaths.MelonDSSaves, "Savestates"), false, MelonDS, MelonDSState, true),
            (HubPaths.DeSmuMESaves, false, DeSmuME, DeSmuMESave, false),
            (Path.Combine(HubPaths.DeSmuMESaves, "Savestates"), false, DeSmuME, DeSmuMEState, true),
            (HubPaths.GbaSaves, false, OrphanSave.GbaPlace, AnySave, false),
            (HubPaths.MGBASaves, false, MGBA, AnySave, false),
            (Path.Combine(HubPaths.MGBASaves, "Savestates"), false, MGBA, MGBAState, true),
            (HubPaths.Roms, true, OrphanSave.RomFolderPlace, AnySave, false),       // GBA emulators save next to the ROM
            (HubPaths.Randomized, true, OrphanSave.RomFolderPlace, AnySave, false),
        ];
        var orphans = new Dictionary<string, OrphanSave>(StringComparer.OrdinalIgnoreCase);
        foreach (var (dir, recursive, place, extension, savestate) in places)
            foreach (var file in AllFiles(dir, recursive))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!extension.IsMatch(Path.GetExtension(file)) || known.Contains(name)) continue;
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }
                if (!orphans.TryGetValue(name, out var orphan)) orphans[name] = orphan = new OrphanSave(name);
                orphan.Files.Add(new OrphanFile(file, place, savestate, info.LastWriteTime, info.Length));
            }
        // Azahar: one SD card folder per game – a folder whose game is gone holds that game's saves
        // (a card of a long game name has a shortened folder name – see Azahar3ds.SdCardOf)
        var cards = new HashSet<string>(known.Select(n => Path.GetFileName(Azahar3ds.SdCardOf(n))), StringComparer.OrdinalIgnoreCase);
        foreach (var card in SafeDirs(HubPaths.AzaharSaves))
        {
            var name = Path.GetFileName(card);
            if (known.Contains(name) || cards.Contains(name)) continue;
            foreach (var file in AllFiles(card, true).Where(f => Path.GetFileName(f) == "main"))
            {
                FileInfo info;
                try { info = new FileInfo(file); }
                catch { continue; }
                if (!orphans.TryGetValue(name, out var orphan)) orphans[name] = orphan = new OrphanSave(name);
                orphan.Files.Add(new OrphanFile(file, Azahar, false, info.LastWriteTime, info.Length));
            }
        }
        return orphans.Values.OrderByDescending(o => o.Newest).ToList();
    }

    /// <summary>
    /// Names that have a ROM: everything in MonHub, plus ROMs from elsewhere that were opened in MonHub's emulators
    /// (their recent-files lists) – those saves are in use even though the ROM lives outside.
    /// </summary>
    static HashSet<string> KnownRomNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rom in RomFiles(HubPaths.Randomized).Concat(RomFiles(HubPaths.Roms)))
            names.Add(Path.GetFileNameWithoutExtension(rom));
        foreach (var rom in RecentRoms())
        {
            try
            {
                if (File.Exists(rom)) names.Add(Path.GetFileNameWithoutExtension(rom));
            }
            catch
            {
                // odd entry in a settings file
            }
        }
        return names;
    }

    static readonly Regex QuotedValue = new("\"((?:[^\"\\\\]|\\\\.)*)\"");

    /// <summary>ROM paths in melonDS' RecentROM list (one line or several, TOML-escaped) and DeSmuME's "Recent Rom N".</summary>
    static IEnumerable<string> RecentRoms()
    {
        var toml = SafeLines(HubPaths.MelonDSToml);
        for (int i = 0; i < toml.Length; i++)
        {
            if (!toml[i].TrimStart().StartsWith("RecentROM", StringComparison.Ordinal)) continue;
            var value = toml[i];
            while (!value.Contains(']') && i + 1 < toml.Length) value += toml[++i];
            foreach (Match m in QuotedValue.Matches(value))
                yield return m.Groups[1].Value.Replace(@"\\", @"\").Replace('/', '\\');
        }
        foreach (var line in SafeLines(SafeFiles(HubPaths.DeSmuME, "*.ini").FirstOrDefault()))
        {
            var t = line.Trim();
            int eq = t.IndexOf('=');
            if (eq > 0 && t.StartsWith("Recent Rom", StringComparison.OrdinalIgnoreCase) && t.Length > eq + 1)
                yield return t[(eq + 1)..].Trim();
        }
    }

    static string[] SafeLines(string? file)
    {
        try { return file != null && File.Exists(file) ? File.ReadAllLines(file) : []; }
        catch { return []; }
    }

    /// <summary>Whole folder trees: a sub folder Windows doesn't let us read is skipped instead of ending the scan.</summary>
    static readonly EnumerationOptions Scan = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    static IEnumerable<string> AllFiles(string dir, bool recursive)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true })
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>melonDS' cheat folder when one is set ([Instance0] CheatFilePath), else null = next to the ROM.</summary>
    static string? MelonDSCheatDir()
    {
        var dir = Setting(HubPaths.MelonDSToml, "Instance0", "CheatFilePath").Replace('/', Path.DirectorySeparatorChar);
        if (dir.Length == 0) return null;
        return Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(HubPaths.MelonDS, dir));
    }

    /// <summary>DeSmuME's cheat folder: [PathSettings] Cheats in its ini, relative to the exe (default .\Cheats).</summary>
    static string DeSmuMECheatDir()
    {
        var ini = SafeFiles(HubPaths.DeSmuME, "*.ini").FirstOrDefault();
        var dir = Setting(ini, "PathSettings", "Cheats");
        if (dir.Length == 0) dir = @".\Cheats";
        return Path.IsPathRooted(dir) ? dir : Path.GetFullPath(Path.Combine(HubPaths.DeSmuME, dir));
    }

    /// <summary>"key=value" (INI) or "key = value" (TOML) in a section of a settings file, without quotes; "" if missing.</summary>
    static string Setting(string? file, string section, string key)
    {
        if (file == null || !File.Exists(file)) return "";
        try
        {
            bool inSection = false;
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (inSection && eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim().Trim('"');
            }
        }
        catch
        {
            // unreadable settings: defaults
        }
        return "";
    }

    static readonly Dictionary<(string Path, DateTime Time), (string Code, string Title)> HeaderCache = new();

    /// <summary>Game code and title from the ROM header (cached per file version – the list is rebuilt often).</summary>
    static (string Code, string Title) Header(string rom)
    {
        var key = (rom, File.GetLastWriteTimeUtc(rom));
        lock (HeaderCache)
            if (HeaderCache.TryGetValue(key, out var known)) return known;
        var info = new RandoApp.RomInfo(rom);
        var result = (info.GameCode, info.HeaderTitle);
        lock (HeaderCache)
            HeaderCache[key] = result;
        return result;
    }

    static Dictionary<string, SaveFile> SavesIn(string dir, string extension, string emulator)
    {
        var saves = new Dictionary<string, SaveFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in SafeFiles(dir, "*" + extension).Where(f => Path.GetExtension(f).Equals(extension, StringComparison.OrdinalIgnoreCase)))
            saves[Path.GetFileNameWithoutExtension(file)] = new SaveFile(file, emulator, File.GetLastWriteTime(file));
        return saves;
    }

    /// <summary>The save, or null when it was never written.</summary>
    /// <summary>
    /// The 3DS games create their save file on the very first start, before the player ever saved – a save of a game
    /// MonHub can read only counts once it has a trainer in it (unknown games: any written save counts).
    /// </summary>
    static SaveFile? Played(SaveFile? save, string gameCode) =>
        save != null && SaveInspector.FamilyOf(gameCode) is { } family && SaveInspector.Read(save.Path, family) == null ? null : save;

    static SaveFile? AzaharSaveOf(string name, ulong titleId)
    {
        var path = Azahar3ds.SavePath(name, titleId);
        try { return File.Exists(path) ? new SaveFile(path, Azahar, File.GetLastWriteTime(path)) : null; }
        catch { return null; }
    }

    static SaveFile? Written(SaveFile? save) => save != null && !IsBlank(save.Path) ? save : null;

    static readonly Dictionary<(string Path, long Size, DateTime Time), bool> BlankCache = new();

    /// <summary>
    /// A save file nobody ever saved into – one byte value throughout (erased flash is 0xFF). DeSmuME creates one as
    /// soon as a game is opened; it must not count as "the newest save". Results are cached per file version.
    /// </summary>
    static bool IsBlank(string path)
    {
        FileInfo info;
        try { info = new FileInfo(path); }
        catch { return false; }
        var key = (path, info.Length, info.LastWriteTimeUtc);
        lock (BlankCache)
            if (BlankCache.TryGetValue(key, out var known)) return known;
        bool blank;
        try
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[stream.Length];
                stream.ReadExactly(bytes);
            }
            var data = SaveFormat.DsvToRaw(bytes);
            blank = data.Length < 16 || data.AsSpan().IndexOfAnyExcept(data[0]) < 0;
        }
        catch
        {
            // Held by the running emulator (mGBA creates the save file when a game starts and keeps it open): a file
            // unchanged since it was created is no save yet; one written later is. Not cached – read properly once free.
            return info.LastWriteTimeUtc - info.CreationTimeUtc < TimeSpan.FromSeconds(10);
        }
        if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromSeconds(10)) // may still be in the middle of being written
            lock (BlankCache)
                BlankCache[key] = blank;
        return blank;
    }

    static IEnumerable<string> RomFiles(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*", Scan).Where(f => HubPaths.RomExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    static GameSystem SystemOf(string rom) => Path.GetExtension(rom).ToLowerInvariant() switch
    {
        ".nds" => GameSystem.DS,
        ".gba" => GameSystem.GBA,
        ".gb" or ".gbc" => GameSystem.GB,
        ".3ds" or ".cci" or ".cxi" => GameSystem.N3DS,
        _ => GameSystem.Other,
    };

    static string[] SafeDirs(string dir)
    {
        try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : []; }
        catch { return []; }
    }

    static string[] SafeFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : []; }
        catch { return []; }
    }
}

public record OrphanFile(string Path, string Place, bool IsSavestate, DateTime Time, long Size);

/// <summary>The save files of one game name that has no ROM anymore (melonDS, DeSmuME, savestates …).</summary>
public class OrphanSave(string name)
{
    public const string GbaPlace = "GBA/GB";
    public const string RomFolderPlace = "ROM-Ordner";

    public string Name { get; } = name;
    public List<OrphanFile> Files { get; } = new();

    public DateTime Newest => Files.Max(f => f.Time);
    public long Size => Files.Sum(f => f.Size);

    /// <summary>Where the save lives – decides the icon colour (melonDS blue, DeSmuME purple …).</summary>
    public string MainPlace => Files.FirstOrDefault(f => !f.IsSavestate)?.Place ?? Files[0].Place;

    /// <summary>The coloured edge of the row, like a save file's label.</summary>
    public Brush PlaceBrush => Avalonia.Media.Brush.Parse(MainPlace switch
    {
        "melonDS" => "#2F8CE0",
        "DeSmuME" => "#7B5BD6",
        GbaPlace => "#3FAE5B",
        RomFolderPlace => "#E0922B",
        _ => "#7C8BA6",
    });

    public List<string> Tags
    {
        get
        {
            var tags = Files.Where(f => !f.IsSavestate).Select(f => f.Place).Distinct().ToList();
            int states = Files.Count(f => f.IsSavestate);
            if (states > 0) tags.Add(states == 1 ? "1 Savestate" : $"{states} Savestates");
            return tags;
        }
    }

    public string TimeText => MonHub.TimeText.Ago(Newest);
    public string SizeText => Size >= 1 << 20 ? $"{Size / (1 << 20)} MB" : $"{Math.Max(1, Size / 1024)} KB";
}

/// <summary>melonDS saves are raw, DeSmuME's .dsv is the raw save plus a 122-byte footer.</summary>
public static class SaveFormat
{
    /// <summary>All Pokémon DS games use 4 Mbit flash.</summary>
    public const int DsSaveSize = 0x80000;
    public const int DsvSaveSize = DsSaveSize + 122;

    static readonly byte[] FooterMarker = Encoding.ASCII.GetBytes("|<--Snip above here to create a raw sav");

    // DeSmuME 0.9.13's footer for 4 Mbit flash – identical in every Pokémon save it wrote (HGSS, DP, Pt, BW2, hacks):
    // text, size fields (4 Mbit, type 6, 3-byte addresses), version 0, cookie
    static readonly byte[] DsvFooter =
    [
        .. Encoding.ASCII.GetBytes("|<--Snip above here to create a raw sav by excluding this DeSmuME savedata footer:"),
        0x01, 0x00, 0x04, 0x00, 0x00, 0x00, 0x08, 0x00, 0x06, 0x00, 0x00, 0x00,
        0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00,
        .. Encoding.ASCII.GetBytes("|-DESMUME SAVE-|"),
    ];

    public static byte[] DsvToRaw(byte[] dsv)
    {
        int at = dsv.AsSpan().IndexOf(FooterMarker);
        return at > 0 ? dsv[..at] : dsv;
    }

    /// <summary>
    /// A raw save as DeSmuME .dsv – only for the 512 KB Pokémon saves, null otherwise. (DeSmuME would import a bare
    /// "<game>.sav" too, but only while no .dsv exists – writing the .dsv leaves no doubt which save it loads.)
    /// </summary>
    public static byte[]? RawToDsv(byte[] save)
    {
        if (save.AsSpan().IndexOf(FooterMarker) >= 0) return save; // already a .dsv
        return save.Length == DsSaveSize ? [.. save, .. DsvFooter] : null;
    }
}

public static class TimeText
{
    public static string Ago(DateTime time)
    {
        var span = DateTime.Now - time;
        if (span.TotalMinutes < 1) return Txt.L("gerade eben", "just now");
        if (span.TotalHours < 1) return Txt.L($"vor {(int)span.TotalMinutes} Min.", $"{(int)span.TotalMinutes} min ago");
        if (span.TotalDays < 1) return Txt.L($"vor {(int)span.TotalHours} Std.", $"{(int)span.TotalHours} h ago");
        if (span.TotalDays < 2) return Txt.L("gestern", "yesterday");
        return Txt.L($"am {time:dd.MM.yyyy}", $"on {time:MMM d, yyyy}");
    }
}

/// <summary>Deleting that can be undone: files go to the Windows recycle bin, on Linux into the trash.</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    const uint FO_DELETE = 3;
    // WANTNUKEWARNING: a file too big for the recycle bin asks first instead of being deleted for good
    const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>True when none of the files (or folders – Azahar's SD card of a game) is left in its place.</summary>
    public static bool Delete(params string[] files)
    {
        static bool There(string path) => File.Exists(path) || Directory.Exists(path);
        var existing = files.Where(There).ToArray();
        if (existing.Length == 0) return true;
        if (!Os.Windows) return Os.Trash(existing) && existing.All(f => !There(f));
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join('\0', existing) + "\0\0", // double-null-terminated list
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };
        SHFileOperation(ref op);
        return existing.All(f => !There(f));
    }
}

/// <summary>Quick checks on ROM files.</summary>
public static class RomCheck
{
    /// <summary>
    /// True when a retail DS ROM still has its secure area (0x4000) encrypted, as many dumps (e.g. the US versions) do.
    /// Decrypted dumps start it with E7FFDEFF E7FFDEFF (or "encryObj"); homebrew has zeros or its ARM9 code elsewhere.
    /// </summary>
    public static bool HasEncryptedSecureArea(string rom)
    {
        try
        {
            using var fs = File.OpenRead(rom);
            if (fs.Length < 0x4008) return false;
            Span<byte> header = stackalloc byte[0x24];
            fs.ReadExactly(header);
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header[0x20..]) != 0x4000) return false; // ARM9 not at the secure area
            fs.Position = 0x4000;
            Span<byte> id = stackalloc byte[8];
            fs.ReadExactly(id);
            ulong value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(id);
            return value != 0xE7FFDEFFE7FFDEFF && value != 0 && !id.SequenceEqual("encryObj"u8);
        }
        catch
        {
            return false; // unreadable: let the emulator decide
        }
    }
}
