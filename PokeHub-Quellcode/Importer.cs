using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace PokeHub;

public enum ImportKind { Rom, SaveDeSmuME, SaveRaw, SaveGba }

/// <summary>What an emulator plays – decides what kind a save file without its ROM is.</summary>
public enum EmuPlatform { Any, DS, GBA }

/// <summary>One file that can be imported, with where it will go.</summary>
public class ImportItem
{
    public ImportKind Kind { get; init; }
    public string SourcePath { get; init; } = "";
    public long Size { get; init; }
    public bool Selected { get; set; } = true;
    /// <summary>Why it is not selected by default (already there, name taken …), or null.</summary>
    public string? Note { get; set; }

    public string Name => Path.GetFileName(SourcePath);
    public string KindText => Kind switch
    {
        ImportKind.Rom => "ROM",
        ImportKind.SaveDeSmuME => "Spielstand (DeSmuME)",
        ImportKind.SaveRaw => "Spielstand (DS)",
        ImportKind.SaveGba => "Spielstand (GBA/GB)",
        _ => "",
    };
    public string SizeText => Size >= 1 << 20 ? $"{Size / (1 << 20)} MB" : $"{Math.Max(1, Size / 1024)} KB";
    public string Status => Note ?? "neu";
}

/// <summary>A place to import from: an emulator install or any folder.</summary>
public record ImportSource(string Label, string Folder, List<string> ExtraFolders)
{
    /// <summary>Battery/save folders among <see cref="ExtraFolders"/> – every save in them belongs to this emulator.</summary>
    public List<string> SaveFolders { get; init; } = new();
    public EmuPlatform Platform { get; init; } = EmuPlatform.Any;
}

/// <summary>
/// Finds existing emulators, ROMs and saves on the PC and copies them into the PokeHub folders.
/// Only ever copies – the player's original files are never changed or deleted, and nothing in PokeHub is overwritten.
/// </summary>
public static class Importer
{
    static readonly string[] RomExt = [".nds", ".gba", ".gbc", ".gb", ".3ds", ".cci", ".cxi"];
    static readonly string[] SkipDirs = ["Windows", "Windows.old", "ProgramData", "$Recycle.Bin", "System Volume Information", "AppData", "node_modules",
                                         "Recovery", "PerfLogs", "WindowsApps", "Pictures", "Bilder", "Music", "Musik", "Videos", "Contacts", "Favorites", "Links", "Searches"];

    /// <summary>Supported emulators: exe name, where their settings live, and what they play.</summary>
    sealed record EmulatorKind(string Label, EmuPlatform Platform, string[] ExePrefixes, string[] ConfigFiles, string[] AppDataDirs);

    static readonly EmulatorKind[] Kinds =
    [
        new("DeSmuME", EmuPlatform.DS, ["DeSmuME"], ["desmume*.ini"], []),
        new("melonDS", EmuPlatform.DS, ["melonDS"], ["melonDS.toml", "melonDS.ini"], ["melonDS"]),
        new("VisualBoyAdvance", EmuPlatform.GBA, ["VisualBoyAdvance", "VBA-", "VBA_", "VBA.", "VBAM"], ["vba*.ini", "vbam.cfg"], ["visualboyadvance-m"]),
        new("mGBA", EmuPlatform.GBA, ["mGBA"], ["config.ini", "qt.ini"], ["mGBA"]),
    ];


    // ---------- finding emulators ----------

    /// <summary>
    /// Looks for the supported emulators: first the programs Windows remembers as started (any drive, any depth),
    /// then the folders where people unpack them (user folders, program folders, top of every drive).
    /// </summary>
    public static List<ImportSource> FindEmulators(CancellationToken cancel = default)
    {
        var exes = new List<string>(ExesWindowsRemembers());

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<(string Path, int Depth)>();
        // real locations of the user folders (they may be moved to OneDrive or another drive), searched deepest first
        foreach (var known in new[] { DownloadsFolder.Value, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) })
            if (!string.IsNullOrEmpty(known)) roots.Add((known, 5));
        foreach (var dir in SafeDirs(profile).Where(d => Path.GetFileName(d).StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase)))
            roots.Add((dir, 5));
        roots.Add((profile, 4)); // own folders like C:\Users\Name\Emulatoren
        roots.Add((Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), 3));
        roots.Add((Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 3));
        roots.Add((Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), 3));
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            roots.Add((drive.RootDirectory.FullName, 3));

        var searched = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (root, depth) in roots.Where(r => Directory.Exists(r.Path)))
        {
            cancel.ThrowIfCancellationRequested();
            exes.AddRange(FindExes(root, depth, searched, cancel));
        }

        var found = new Dictionary<string, ImportSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in exes)
        {
            cancel.ThrowIfCancellationRequested();
            if (FromEmulatorExe(exe) is { } src) found.TryAdd(src.Folder, src);
        }
        return found.Values.OrderBy(s => s.Label).ThenBy(s => s.Folder, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool IsEmulatorExe(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && KindOf(path) != null;

    static EmulatorKind? KindOf(string exe)
    {
        var name = Path.GetFileName(exe);
        return Kinds.FirstOrDefault(k => k.ExePrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Emulators Windows has seen being started (Explorer's app-name cache and the compatibility assistant) – that finds them
    /// wherever they are. Read-only; only entries whose file name is a supported emulator are used.
    /// </summary>
    static IEnumerable<string> ExesWindowsRemembers()
    {
        string[] keys =
        [
            @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
        ];
        var temp = Path.GetTempPath();
        foreach (var keyPath in keys)
        {
            string[] names;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(keyPath);
                names = key?.GetValueNames() ?? [];
            }
            catch
            {
                continue;
            }
            foreach (var name in names)
            {
                int at = name.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); // MuiCache: "C:\…\melonDS.exe.FriendlyAppName"
                if (at <= 0) continue;
                var exe = name[..(at + 4)];
                // skip programs started straight out of a zip (Windows unpacks those into Temp)
                if (IsEmulatorExe(exe) && !exe.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                    yield return exe;
            }
        }
    }

    /// <summary>Emulator exes below <paramref name="root"/>. <paramref name="searched"/> remembers how deep each folder was already searched.</summary>
    static IEnumerable<string> FindExes(string root, int depth, Dictionary<string, int> searched, CancellationToken cancel)
    {
        var stack = new Stack<(string Dir, int Level)>();
        stack.Push((root, 0));
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (dir, level) = stack.Pop();
            int left = depth - level;
            bool listed = searched.TryGetValue(dir, out var before);
            if (listed && before >= left) continue;
            searched[dir] = left;
            if (IsInsideHub(dir)) continue;
            if (!listed)
                foreach (var exe in SafeFiles(dir, "*.exe"))
                    if (IsEmulatorExe(exe)) yield return exe;
            if (left <= 0) continue;
            foreach (var sub in SafeDirs(dir))
                if (!IsSkipped(sub)) stack.Push((sub, level + 1));
        }
    }

    /// <summary>Emulator folder plus the ROM/save folders its settings point to.</summary>
    public static ImportSource? FromEmulatorExe(string exe)
    {
        var kind = KindOf(exe);
        if (kind == null || !File.Exists(exe)) return null;
        var folder = Path.GetDirectoryName(exe)!;
        if (IsInsideHub(folder)) return null;

        var folders = new List<string>();
        var saveFolders = new List<string>();
        foreach (var config in ConfigFiles(kind, folder))
            ReadConfigFolders(config, folder, folders, saveFolders);
        return new ImportSource(kind.Label, folder, Clean(folders, folder))
        {
            Platform = kind.Platform,
            SaveFolders = Clean(saveFolders, folder),
        };
    }

    /// <summary>The emulator's settings files: next to the exe (portable) or in its AppData folder.</summary>
    static IEnumerable<string> ConfigFiles(EmulatorKind kind, string exeDir)
    {
        var dirs = new List<string> { exeDir };
        foreach (var name in kind.AppDataDirs)
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name));
        }
        return dirs.Where(Directory.Exists)
            .SelectMany(d => kind.ConfigFiles.SelectMany(pattern => SafeFiles(d, pattern)))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // keys that name a ROM or save folder or a recently played game – in every emulator's own spelling:
    // DeSmuME Roms/Battery/"Recent Rom 1", melonDS LastROMFolder/SaveFilePath/RecentROM, VBA romDir/batteryDir/recent0,
    // VBA-M [Recent] file1, mGBA lastDirectory/savegamePath and qt.ini [mru] 0=…
    static readonly Regex FolderKey = new(@"rom|recent|mru|last|batt|save|file|^\d+$", RegexOptions.IgnoreCase);
    static readonly Regex SaveKey = new(@"batt|savegame|savefile", RegexOptions.IgnoreCase);
    static readonly Regex Quoted = new("\"([^\"]*)\"");

    /// <summary>Reads "key = value" lines of an INI/TOML settings file and collects the folders the values point to.</summary>
    static void ReadConfigFolders(string file, string exeDir, List<string> folders, List<string> saveFolders)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch { return; }
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0 || line[0] is '[' or '#' or ';') continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            // melonDS writes longer lists over several lines: RecentROM = [ "…", "…", ]
            if (value.StartsWith('['))
                while (!value.Contains(']') && i + 1 < lines.Length)
                    value += " " + lines[++i].Trim();
            if (value.Length == 0 || !FolderKey.IsMatch(key)) continue;
            // TOML list ["a", "b"] or a single (maybe quoted) value
            var values = value.StartsWith('[') ? Quoted.Matches(value).Select(m => m.Groups[1].Value) : [value.Trim('"')];
            foreach (var v in values)
                if (ToFolder(v, exeDir) is { } dir)
                {
                    folders.Add(dir);
                    if (SaveKey.IsMatch(key)) saveFolders.Add(dir);
                }
        }
    }

    /// <summary>The existing folder a settings value points to (a file's folder for a file), or null.</summary>
    static string? ToFolder(string value, string exeDir)
    {
        var p = value.Replace('/', '\\');
        if (p.Length < 2 || p.StartsWith(@"\\")) return null; // empty, or a network path (hangs while the NAS is off)
        p = p.Replace(@"\\", @"\");                             // TOML escapes
        try
        {
            if (!Path.IsPathFullyQualified(p))
            {
                if (!p.StartsWith('.')) return null;            // only ".\Battery"-style folders next to the exe
                p = Path.GetFullPath(Path.Combine(exeDir, p));
            }
            if (new DriveInfo(Path.GetPathRoot(p)!).DriveType is DriveType.Network or DriveType.NoRootDirectory) return null;
            var dir = Directory.Exists(p) ? p : Path.GetDirectoryName(p);
            if (dir == null || !Directory.Exists(dir)) return null;
            dir = dir.TrimEnd('\\');
            if (dir.Length <= 2) return null;                   // a whole drive is far too much
            return IsSameOrInside(dir, Path.GetTempPath()) ? null : dir; // ROMs opened straight from a zip live in Temp only briefly
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Existing folders outside PokeHub, without the emulator folder itself and its subfolders (those are scanned anyway).</summary>
    static List<string> Clean(IEnumerable<string> dirs, string main) =>
        dirs.Where(d => d.Length > 0 && Directory.Exists(d) && !IsInsideHub(d) && !IsSameOrInside(d, main))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ---------- scanning ----------

    /// <summary>ROMs and saves in the given folders (a few levels deep), compared against what PokeHub already has.</summary>
    public static List<ImportItem> Scan(IEnumerable<ImportSource> sources, CancellationToken cancel = default)
    {
        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        // .sav files in an emulator's own or battery folder – elsewhere a .sav only counts next to a ROM of the same name,
        // so a ROM folder like "Downloads" doesn't bring along the saves of other PC games
        var ownedSaves = new Dictionary<string, EmuPlatform>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in sources)
        {
            var saveDirs = new HashSet<string>(src.SaveFolders, StringComparer.OrdinalIgnoreCase);
            if (!IsBroadFolder(src.Folder)) saveDirs.Add(src.Folder);
            foreach (var dir in new[] { src.Folder }.Concat(src.ExtraFolders))
            {
                bool allSaves = src.Platform == EmuPlatform.Any || saveDirs.Contains(dir); // a folder the player picked: take everything
                foreach (var f in ScanFiles(dir, 3, cancel))
                {
                    files.TryAdd(f.FullName, f);
                    if (allSaves && f.Extension.Equals(".sav", StringComparison.OrdinalIgnoreCase))
                        ownedSaves.TryAdd(f.FullName, src.Platform);
                }
            }
        }

        var romBases = files.Values.Where(f => RomExt.Contains(f.Extension.ToLowerInvariant()))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Extension.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

        var items = new List<ImportItem>();
        var seenRoms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files.Values.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var ext = f.Extension.ToLowerInvariant();
            if (RomExt.Contains(ext))
            {
                if (!seenRoms.Add($"{f.Name}|{f.Length}")) continue; // same ROM found twice
                var item = new ImportItem { Kind = ImportKind.Rom, SourcePath = f.FullName, Size = f.Length };
                var target = Path.Combine(HubPaths.Roms, f.Name);
                if (File.Exists(target))
                {
                    item.Note = new FileInfo(target).Length == f.Length ? "schon im MonHub" : "Name im MonHub schon belegt";
                    item.Selected = false;
                }
                items.Add(item);
            }
            else if (ext == ".dsv")
                items.Add(SaveItem(ImportKind.SaveDeSmuME, f));
            else if (ext == ".sav")
            {
                // .sav is used by many programs: decide by the ROM with the same name, else by the emulator it belongs to
                var romExt = romBases.GetValueOrDefault(Path.GetFileNameWithoutExtension(f.Name));
                bool owned = ownedSaves.TryGetValue(f.FullName, out var platform);
                if (romExt == null && !owned) continue;
                var kind = romExt != null
                    ? (romExt is ".gba" or ".gbc" or ".gb" ? ImportKind.SaveGba : ImportKind.SaveRaw)
                    : platform switch
                    {
                        EmuPlatform.DS => ImportKind.SaveRaw,
                        EmuPlatform.GBA => ImportKind.SaveGba,
                        _ => LooksLikeDsSave(f) ? ImportKind.SaveRaw : ImportKind.SaveGba,
                    };
                items.Add(SaveItem(kind, f));
            }
        }
        return items;
    }

    /// <summary>DS saves are 8 KB – 1 MB in power-of-two sizes; GBA ones are ≤ 128 KB. Unknown .sav of 256 KB+ are DS.</summary>
    static bool LooksLikeDsSave(FileInfo f) => f.Length >= 256 * 1024 && (f.Length & (f.Length - 1)) == 0;

    static ImportItem SaveItem(ImportKind kind, FileInfo f)
    {
        var item = new ImportItem { Kind = kind, SourcePath = f.FullName, Size = f.Length };
        if (TargetsFor(item).All(t => File.Exists(t.Path)))
        {
            item.Note = "schon im MonHub";
            item.Selected = false;
        }
        return item;
    }

    static IEnumerable<FileInfo> ScanFiles(string dir, int depth, CancellationToken cancel)
    {
        if (!Directory.Exists(dir) || IsInsideHub(dir)) yield break;
        var stack = new Stack<(string Dir, int Level)>();
        stack.Push((dir, 0));
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (d, level) = stack.Pop();
            if (IsInsideHub(d)) continue;
            foreach (var f in SafeFiles(d, "*"))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (RomExt.Contains(ext) || ext is ".dsv" or ".sav")
                {
                    FileInfo? info = null;
                    try { info = new FileInfo(f); } catch { }
                    if (info != null) yield return info;
                }
            }
            if (level >= depth) continue;
            foreach (var sub in SafeDirs(d))
                if (!IsSkipped(sub))
                    stack.Push((sub, level + 1));
        }
    }

    // ---------- importing ----------

    enum Conversion { None, DsvToRaw, RawToDsv }

    /// <summary>
    /// Where a file ends up. A save goes to ONE emulator – the one der Randomizer uses (melonDS unless set to DeSmuME):
    /// two copies would drift apart, and deleting one would leave the other. "Spiel auswählen" moves it when needed.
    /// </summary>
    static IEnumerable<(string Path, Conversion Convert)> TargetsFor(ImportItem item)
    {
        var baseName = Path.GetFileNameWithoutExtension(item.SourcePath);
        bool toDeSmuME = item.Kind is ImportKind.SaveDeSmuME or ImportKind.SaveRaw && GameLibrary.MainEmulator() == GameLibrary.DeSmuME;
        switch (item.Kind)
        {
            case ImportKind.Rom:
                yield return (Path.Combine(HubPaths.Roms, item.Name), Conversion.None);
                break;
            case ImportKind.SaveDeSmuME:
                yield return toDeSmuME
                    ? (GameLibrary.SavePath(GameLibrary.DeSmuME, baseName), Conversion.None)
                    : (GameLibrary.SavePath(GameLibrary.MelonDS, baseName), Conversion.DsvToRaw);
                break;
            case ImportKind.SaveRaw:
                // for DeSmuME as a .dsv (512 KB Pokémon saves) – a bare .sav would lose against any .dsv already there
                yield return toDeSmuME && item.Size == SaveFormat.PokemonSaveSize
                    ? (GameLibrary.SavePath(GameLibrary.DeSmuME, baseName), Conversion.RawToDsv)
                    : (GameLibrary.SavePath(GameLibrary.MelonDS, baseName), Conversion.None);
                break;
            case ImportKind.SaveGba:
                yield return (Path.Combine(HubPaths.MGBASaves, item.Name), Conversion.None); // where mGBA looks
                break;
        }
    }

    public record Result(int Copied, int Skipped, List<string> Problems);

    public static Result Import(IEnumerable<ImportItem> items, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        int copied = 0, skipped = 0;
        var problems = new List<string>();
        foreach (var item in items.Where(i => i.Selected))
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report(item.Name);
            bool any = false, failed = false;
            foreach (var (target, convert) in TargetsFor(item))
            {
                // copied under a temporary name first: an interrupted copy (disk full, drive unplugged) never leaves a
                // cut-off file that the next import would take as "already there"
                var tmp = target + ".pokehub-tmp";
                try
                {
                    if (File.Exists(target)) continue; // never overwrite anything in PokeHub
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    if (convert == Conversion.None)
                        File.Copy(item.SourcePath, tmp, overwrite: true);
                    else
                    {
                        var data = File.ReadAllBytes(item.SourcePath);
                        File.WriteAllBytes(tmp, convert == Conversion.DsvToRaw ? SaveFormat.DsvToRaw(data) : SaveFormat.RawToDsv(data)!);
                    }
                    // keep the original date – "Weiterspielen" goes by when a game was saved
                    File.SetLastWriteTime(tmp, File.GetLastWriteTime(item.SourcePath));
                    File.Move(tmp, target);
                    any = true;
                }
                catch (Exception ex)
                {
                    failed = true;
                    problems.Add($"{item.Name}: {ex.Message}");
                    try { File.Delete(tmp); } catch { /* nothing more to do */ }
                }
            }
            if (any) copied++; else if (!failed) skipped++;
        }
        return new Result(copied, skipped, problems);
    }

    // ---------- helpers ----------

    static bool IsInsideHub(string path) => IsSameOrInside(path, HubPaths.Root);

    static bool IsSameOrInside(string path, string folder) =>
        (path.TrimEnd('\\') + "\\").StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>System/junk folders and hidden dot-folders (.git, .cache …) are never searched.</summary>
    static bool IsSkipped(string dir)
    {
        var name = Path.GetFileName(dir);
        return name.StartsWith('.') || SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Folders full of unrelated files – an emulator lying directly in one of them doesn't make all its .sav files game saves.</summary>
    static bool IsBroadFolder(string dir)
    {
        var d = dir.TrimEnd('\\');
        if (d.Length <= 2) return true;
        string?[] broad =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            DownloadsFolder.Value,
        ];
        return broad.Any(b => !string.IsNullOrEmpty(b) && d.Equals(b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The real Downloads folder (can be moved to another drive; .NET has no SpecialFolder for it).</summary>
    static readonly Lazy<string?> DownloadsFolder = new(() =>
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B"); // FOLDERID_Downloads
        if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    });

    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    static string[] SafeFiles(string dir, string pattern)
    {
        try { return Directory.GetFiles(dir, pattern); }
        catch { return []; }
    }

    static string[] SafeDirs(string dir)
    {
        try
        {
            return Directory.GetDirectories(dir).Where(d =>
            {
                try { return (new DirectoryInfo(d).Attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) == 0; }
                catch { return false; }
            }).ToArray();
        }
        catch { return []; }
    }
}
