using System.IO;
using System.Text;

namespace RandoApp;

public enum RomPlatform { Unknown, Nds, Gba, Gb }

/// <summary>A ROM file in the ROM folder plus what its header says about it.</summary>
public class RomInfo
{
    public static readonly string[] Extensions = [".nds", ".gba", ".gbc", ".gb", ".3ds", ".cia", ".cxi"];

    /// <summary>File type groups for the "which ROMs to list" setting: id, label, extensions.</summary>
    public static readonly (string Id, string Label, string[] Extensions)[] FileTypes =
    [
        ("nds", "DS (.nds)", [".nds"]),
        ("gba", "GBA (.gba)", [".gba"]),
        ("gbc", "Game Boy Color (.gbc)", [".gbc"]),
        ("gb", "Game Boy (.gb)", [".gb"]),
        ("3ds", "3DS (.3ds, .cia, .cxi)", [".3ds", ".cia", ".cxi"]),
    ];

    /// <summary>Extensions belonging to the selected type ids (all when nothing is configured).</summary>
    public static HashSet<string> ExtensionsFor(IEnumerable<string>? typeIds)
    {
        var ids = typeIds?.ToHashSet() ?? [];
        var chosen = FileTypes.Where(t => ids.Count == 0 || ids.Contains(t.Id)).SelectMany(t => t.Extensions);
        return chosen.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Path { get; }
    public string FileName => System.IO.Path.GetFileName(Path);
    public RomPlatform Platform { get; }
    public string HeaderTitle { get; } = "";
    /// <summary>4-letter game code, e.g. "IRED" = Pokémon Schwarz 2 (DE).</summary>
    public string GameCode { get; } = "";
    /// <summary>NDS unit code: 0 = DS, 2/3 = DSi-enhanced.</summary>
    public byte UnitCode { get; }
    /// <summary>NDS header byte 0x1E. Needed where regions share a game code (Diamond/Pearl: 5 = USA, 13 = Europe).</summary>
    public byte RomVersion { get; }

    public RomInfo(string path)
    {
        Path = path;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        try
        {
            using var fs = File.OpenRead(path);
            var header = new byte[0x150];
            int read = fs.Read(header, 0, header.Length);
            switch (ext)
            {
                case ".nds" when read >= 0x20:
                    Platform = RomPlatform.Nds;
                    HeaderTitle = Ascii(header, 0x00, 12);
                    GameCode = Ascii(header, 0x0C, 4);
                    UnitCode = header[0x12];
                    RomVersion = header[0x1E];
                    break;
                case ".gba" when read >= 0xB0:
                    Platform = RomPlatform.Gba;
                    HeaderTitle = Ascii(header, 0xA0, 12);
                    GameCode = Ascii(header, 0xAC, 4);
                    break;
                case ".gb" or ".gbc" when read >= 0x144:
                    Platform = RomPlatform.Gb;
                    HeaderTitle = Ascii(header, 0x134, 11);
                    break;
            }
        }
        catch
        {
            // Unreadable ROM: still listed, randomizer will report the real problem.
        }
    }

    static string Ascii(byte[] data, int offset, int length) =>
        Encoding.ASCII.GetString(data, offset, length).TrimEnd('\0', ' ');

    /// <summary>Sub folder below the ROM folder (e.g. "Deutsch"), shown in front of the name.</summary>
    public string Folder { get; init; } = "";

    public string Display
    {
        get
        {
            var name = string.IsNullOrEmpty(GameCode) ? FileName : $"{FileName}   ({GameCode})";
            return Folder.Length == 0 ? name : $"{Folder}  ›  {name}";
        }
    }

    public override string ToString() => Display;
}
