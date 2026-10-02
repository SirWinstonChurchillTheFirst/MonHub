using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;

namespace PokeHub;

/// <summary>Save layout of a game family (the Game Boy, GBA, DS and 3DS Pokémon games MonHub knows).</summary>
public enum SaveFamily { DP, Pt, HGSS, BW, B2W2, XY, ORAS, SM, USUM, RS, E, FRLG, RBY, GS, C }

public record PartyMember(int Species, int Level, bool IsEgg, string Nickname);

/// <summary>The save's own Dex: one seen and one caught flag per national number (1 … <see cref="Size"/>).</summary>
public sealed class Pokedex(int size, byte[] seen, byte[] caught)
{
    /// <summary>How many Pokémon the game knows (493 in Gen 4, 649 in Gen 5, 721 in Gen 6, 802/807 in Gen 7).</summary>
    public int Size { get; } = size;
    public bool Caught(int dex) => Flag(caught, dex);
    /// <summary>Seen – a caught Pokémon always counts as seen.</summary>
    public bool Seen(int dex) => Flag(seen, dex) || Flag(caught, dex);
    bool Flag(byte[] bits, int dex) => dex >= 1 && dex <= Size && (dex - 1) >> 3 < bits.Length && (bits[(dex - 1) >> 3] >> ((dex - 1) & 7) & 1) == 1;
}

/// <summary>What the game itself shows on its "continue" screen and trainer card – read straight from the save.</summary>
public record SaveSummary(
    SaveFamily Family, string Trainer, int TrainerId, uint Money,
    int Hours, int Minutes, int Seconds,
    int Badges, int BadgeSlots, int DexSeen, int DexCaught,
    IReadOnlyList<PartyMember> Party, Pokedex Dex)
{
    public string PlayTime => $"{Hours}:{Minutes:00}";
    public TimeSpan PlayTimeSpan => new(Hours, Minutes, Seconds);
}

/// <summary>
/// Reads trainer, play time, badges, Dex and party from Gen 4 (Diamant/Perl, Platin, HeartGold/SoulSilver) and
/// Gen 5 (Schwarz/Weiß 1+2) saves, and from the 3DS games (X/Y, ΩR/αS, Sonne/Mond, US/UM – the "main" file of the
/// save data). Read-only; offsets and results were checked against PKHeX with real saves.
/// Formats: Project Pokémon save/PKM documentation (block footers with save counter + CRC16-CCITT, LCRNG-encrypted PKM).
/// </summary>
public static class SaveInspector
{
    public static SaveFamily? FamilyOf(string gameCode) => gameCode.Length < 3 ? null : gameCode[..3].ToUpperInvariant() switch
    {
        // GBA product codes (AGB-BPGD …)
        "AXV" or "AXP" => SaveFamily.RS,
        "BPE" => SaveFamily.E,
        "BPR" or "BPG" => SaveFamily.FRLG,
        "ADA" or "APA" => SaveFamily.DP,
        "CPU" => SaveFamily.Pt,
        "IPK" or "IPG" => SaveFamily.HGSS,
        "IRB" or "IRA" => SaveFamily.BW,
        "IRE" or "IRD" => SaveFamily.B2W2,
        // 3DS product codes (CTR-P-EKJA …)
        "EKJ" or "EK2" => SaveFamily.XY,
        "ECR" or "ECL" => SaveFamily.ORAS,
        "BND" or "BNE" => SaveFamily.SM,
        "A2A" or "A2B" => SaveFamily.USUM,
        _ => null,
    };

    /// <summary>
    /// Game Boy games have no product code – their family comes from the title in the header ("POKEMON RED",
    /// "POKEMON_GLD", "PM_CRYSTAL" …, the same in every western language).
    /// </summary>
    public static SaveFamily? FamilyOfTitle(string title)
    {
        var t = title.ToUpperInvariant();
        if (t.Contains("CRYSTAL")) return SaveFamily.C;
        if (t.StartsWith("POKEMON_GLD") || t.StartsWith("POKEMON_SLV")) return SaveFamily.GS;
        if (t.StartsWith("POKEMON RED") || t.StartsWith("POKEMON BLUE") || t.StartsWith("POKEMON YEL") || t.StartsWith("POKEMON GREEN"))
            return SaveFamily.RBY;
        return null;
    }

    static readonly Dictionary<(string Path, long Size, DateTime Time, SaveFamily Family), SaveSummary?> Cache = new();
    const long MaxSaveSize = 4 * 1024 * 1024;

    /// <summary>The summary of a save (.sav or DeSmuME .dsv), or null if it is empty, damaged or not of that game.</summary>
    public static SaveSummary? Read(string path, SaveFamily family)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch
        {
            return null;
        }
        if (info.Length > MaxSaveSize) return null; // the biggest save (Ultra Sun/Moon, as .dsv) is < 1 MB
        var key = (path.ToLowerInvariant(), info.Length, info.LastWriteTimeUtc, family);
        lock (Cache)
            if (Cache.TryGetValue(key, out var known)) return known;

        SaveSummary? result = null;
        try
        {
            var data = SaveFormat.DsvToRaw(File.ReadAllBytes(path));
            if (family is SaveFamily.XY or SaveFamily.ORAS or SaveFamily.SM or SaveFamily.USUM)
                result = Read3ds(data, family);
            else if (family is SaveFamily.RS or SaveFamily.E or SaveFamily.FRLG)
                result = data.Length >= 0x20000 ? ReadGen3(data, family) : null;
            else if (family == SaveFamily.RBY)
                result = data.Length >= 0x8000 ? ReadGen1(data) : null;
            else if (family is SaveFamily.GS or SaveFamily.C)
                result = data.Length >= 0x8000 ? ReadGen2(data, family) : null;
            else if (data.Length >= 0x80000)
                result = family is SaveFamily.BW or SaveFamily.B2W2 ? ReadGen5(data, family) : ReadGen4(data, family);
        }
        catch
        {
            result = null; // being written right now, or not a save of this game
        }
        // a file written in the last seconds may have been read half-way through the emulator's write (a 3DS save is
        // 400+ KB, and the timestamp may not move again for the rest of that write) – read it again next time
        if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromSeconds(10))
            lock (Cache)
            {
                if (Cache.Count > 2000) Cache.Clear(); // every write of a save is a new key – a long session must not pile them up
                Cache[key] = result;
            }
        return result;
    }

    // ---------------- Gen 1 (Rot, Blau, Gelb) ----------------

    /// <summary>
    /// The western Game Boy save (32 KB): trainer, Dex, money (BCD), badges, ID (big-endian), play time, party;
    /// one checksum over 0x2598–0x3522. Japanese saves are laid out differently and fail the checksum.
    /// </summary>
    static SaveSummary? ReadGen1(byte[] d)
    {
        const int start = 0x2598, check = 0x3523, party = 0x2F2C;
        byte sum = 0;
        for (int i = start; i < check; i++) sum += d[i];
        if ((byte)~sum != d[check]) return null; // empty, damaged or not a western save

        var name = Gen12Text(d.AsSpan(0x2598, 11), Gen1Chars);
        if (name.Length == 0) return null;
        var caught = d.AsSpan(0x25A3, 19).ToArray();
        var seen = d.AsSpan(0x25B6, 19).ToArray();
        uint money = (uint)(Bcd(d[0x25F3]) * 10000 + Bcd(d[0x25F4]) * 100 + Bcd(d[0x25F5]));

        var members = new List<PartyMember>();
        int count = Math.Min(6, (int)d[party]);
        for (int i = 0; i < count; i++)
        {
            var mon = d.AsSpan(party + 8 + i * 44, 44);
            int species = mon[0] < Gen1National.Length ? Gen1National[mon[0]] : 0;
            if (species == 0) continue;
            members.Add(new PartyMember(species, mon[0x21], false, Gen12Text(d.AsSpan(party + 8 + 6 * 44 + 6 * 11 + i * 11, 11), Gen1Chars)));
        }
        return new SaveSummary(SaveFamily.RBY, name, BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x2605)), money,
            d[0x2CED], d[0x2CEF], d[0x2CF0],
            BitOperations.PopCount(d[0x2602]), 8, CountBits(seen, 151), CountBits(caught, 151), members,
            new Pokedex(151, seen, caught));
    }

    static int Bcd(byte b) => (b >> 4) * 10 + (b & 0xF);

    /// <summary>Gen 1 numbers its Pokémon in its own order (internal 1 = Rhydon = #112). From PKHeX's SpeciesConverter.</summary>
    static readonly byte[] Gen1National =
    [
        0, 112, 115, 32, 35, 21, 100, 34, 80, 2, 103, 108, 102, 88, 94, 29, 31, 104, 111, 131, 59, 151, 130, 90,
        72, 92, 123, 120, 9, 127, 114, 0, 0, 58, 95, 22, 16, 79, 64, 75, 113, 67, 122, 106, 107, 24, 47, 54,
        96, 76, 0, 126, 0, 125, 82, 109, 0, 56, 86, 50, 128, 0, 0, 0, 83, 48, 149, 0, 0, 0, 84, 60,
        124, 146, 144, 145, 132, 52, 98, 0, 0, 0, 37, 38, 25, 26, 0, 0, 147, 148, 140, 141, 116, 117, 0, 0,
        27, 28, 138, 139, 39, 40, 133, 136, 135, 134, 66, 41, 23, 46, 61, 62, 13, 14, 15, 0, 85, 57, 51, 49,
        87, 0, 0, 10, 11, 12, 68, 0, 55, 97, 42, 150, 143, 129, 0, 0, 89, 0, 99, 91, 0, 101, 36, 110,
        53, 105, 0, 93, 63, 65, 17, 18, 121, 1, 3, 73, 0, 118, 119, 0, 0, 0, 0, 77, 78, 19, 20, 33,
        30, 74, 137, 142, 0, 81, 0, 0, 4, 7, 5, 8, 6, 0, 0, 0, 0, 43, 44, 45, 69, 70, 71,
    ];

    // ---------------- Gen 2 (Gold, Silber, Kristall) ----------------

    /// <summary>Where Gold/Silber and Kristall keep things (western versions; from PKHeX's SAV2 offsets).</summary>
    static (int Time, int Money, int Badges, int Party, int Caught, int Seen, int ChecksumEnd, int Checksum) Gen2Layout(SaveFamily f) => f == SaveFamily.C
        ? (0x2052, 0x23DC, 0x23E5, 0x2865, 0x2A27, 0x2A47, 0x2B82, 0x2D0D)
        : (0x2053, 0x23DB, 0x23E4, 0x288A, 0x2A4C, 0x2A6C, 0x2D68, 0x2D69);

    static SaveSummary? ReadGen2(byte[] d, SaveFamily family)
    {
        var l = Gen2Layout(family);
        ushort sum = 0;
        for (int i = 0x2009; i <= l.ChecksumEnd; i++) sum += d[i];
        if (sum != BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(l.Checksum))) return null; // empty, damaged or not western

        var name = Gen12Text(d.AsSpan(0x200B, 11), Gen2Chars);
        if (name.Length == 0) return null;
        var caught = d.AsSpan(l.Caught, 32).ToArray();
        var seen = d.AsSpan(l.Seen, 32).ToArray();
        uint money = (uint)(d[l.Money] << 16 | d[l.Money + 1] << 8 | d[l.Money + 2]);

        var members = new List<PartyMember>();
        int count = Math.Min(6, (int)d[l.Party]);
        for (int i = 0; i < count; i++)
        {
            var mon = d.AsSpan(l.Party + 8 + i * 48, 48);
            bool egg = d[l.Party + 1 + i] == 0xFD; // the species list says "egg"
            int species = mon[0];
            if (species is 0 or > 251) continue;
            members.Add(new PartyMember(species, mon[0x1F], egg, Gen12Text(d.AsSpan(l.Party + 8 + 6 * 48 + 6 * 11 + i * 11, 11), Gen2Chars)));
        }
        return new SaveSummary(family, name, BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(0x2009)), money,
            BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(l.Time)), d[l.Time + 2], d[l.Time + 3], // hours are 2 bytes here
            BitOperations.PopCount(d[l.Badges]) + BitOperations.PopCount(d[l.Badges + 1]), 16, // Johto + Kanto
            CountBits(seen, 251), CountBits(caught, 251), members, new Pokedex(251, seen, caught));
    }

    // ---------------- Gen 3 (GBA) ----------------

    const int Gen3Species = 386;

    /// <summary>Bytes of each of the 14 save sections that the checksum covers (by section ID).</summary>
    static readonly int[] Gen3SectionSize = [0xF2C, 0xF80, 0xF80, 0xF80, 0xF08, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0xF80, 0x7D0];

    /// <summary>In the "large" block (sections 1–4 one after the other): money, party count, event flags; first badge flag.</summary>
    static (int Money, int Party, int Flags, int FirstBadge) Gen3Layout(SaveFamily f) => f switch
    {
        SaveFamily.RS => (0x490, 0x234, 0x1220, 0x807),
        SaveFamily.E => (0x490, 0x234, 0x1270, 0x867),
        _ => (0x290, 0x34, 0xEE0, 0x820), // FRLG
    };

    static SaveSummary? ReadGen3(byte[] d, SaveFamily family)
    {
        // two save slots (0x0000 / 0xE000) of 14 sections of 4 KB, stored in rotating order. Section footer at 0xFF4:
        // ID, checksum, 0x08012025, save counter. The slot whose sections are all valid and that was saved last counts.
        byte[][]? sections = null;
        uint best = 0;
        foreach (int slot in new[] { 0, 0xE000 })
        {
            var found = new byte[14][];
            uint counter = 0;
            bool valid = true;
            for (int i = 0; i < 14 && valid; i++)
            {
                int at = slot + i * 0x1000;
                int id = U16(d, at + 0xFF4);
                valid = U32(d, at + 0xFF8) == 0x08012025 && id < 14 && found[id] == null
                        && Gen3Checksum(d.AsSpan(at, Gen3SectionSize[id])) == U16(d, at + 0xFF6);
                if (!valid) break;
                found[id] = d.AsSpan(at, 0xF80).ToArray();
                counter = U32(d, at + 0xFFC);
            }
            if (valid && (sections == null || counter > best))
            {
                sections = found;
                best = counter;
            }
        }
        if (sections == null) return null;

        var small = sections[0]; // trainer, play time, Dex
        var name = Gen3Text(small.AsSpan(0, 7));
        if (name.Length == 0) return null;
        var large = new byte[4 * 0xF80];
        for (int i = 0; i < 4; i++) sections[1 + i].CopyTo(large, i * 0xF80);

        var l = Gen3Layout(family);
        // Emerald and FireRed/LeafGreen hide the money with the save's security key
        uint key = family switch { SaveFamily.E => U32(small, 0xAC), SaveFamily.FRLG => U32(small, 0xF20), _ => 0 };
        int badges = 0;
        for (int b = 0; b < 8; b++)
        {
            int flag = l.FirstBadge + b;
            if ((large[l.Flags + (flag >> 3)] >> (flag & 7) & 1) == 1) badges++;
        }
        var party = new List<PartyMember>();
        int count = (int)Math.Min(6, U32(large, l.Party));
        for (int i = 0; i < count; i++)
            if (ReadMon3(large.AsSpan(l.Party + 4 + i * 100, 100)) is { } mon) party.Add(mon);

        var caught = small.AsSpan(0x28, 0x34).ToArray();
        var seen = small.AsSpan(0x5C, 0x34).ToArray();
        return new SaveSummary(family, name, U16(small, 0x0A), U32(large, l.Money) ^ key,
            U16(small, 0x0E), small[0x10], small[0x11],
            badges, 8, CountBits(seen, Gen3Species), CountBits(caught, Gen3Species), party,
            new Pokedex(Gen3Species, seen, caught));
    }

    /// <summary>Section checksum: 32-bit sum of the words, both halves added.</summary>
    static ushort Gen3Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        for (int i = 0; i + 3 < data.Length; i += 4) sum += BinaryPrimitives.ReadUInt32LittleEndian(data[i..]);
        return (ushort)((sum >> 16) + (sum & 0xFFFF));
    }

    /// <summary>PK3 party entry: 4 substructures of 12 bytes (growth, attacks, EVs, misc), shuffled and XORed with PID ^ OT ID.</summary>
    static PartyMember? ReadMon3(ReadOnlySpan<byte> raw)
    {
        uint pid = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        uint key = pid ^ BinaryPrimitives.ReadUInt32LittleEndian(raw[4..]);
        var data = raw.Slice(0x20, 48).ToArray();
        for (int i = 0; i < data.Length; i += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) ^ key);
        ushort sum = 0;
        for (int i = 0; i < data.Length; i += 2) sum += BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i));
        if (sum != BinaryPrimitives.ReadUInt16LittleEndian(raw[0x1C..])) return null; // empty slot or damaged

        var order = BlockOrder[pid % 24];
        var blocks = new byte[48];
        for (int stored = 0; stored < 4; stored++)
            data.AsSpan(stored * 12, 12).CopyTo(blocks.AsSpan((order[stored] - 'A') * 12, 12));
        int species = National3(BinaryPrimitives.ReadUInt16LittleEndian(blocks));                // growth +0
        if (species <= 0) return null;
        bool egg = ((BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(36 + 4)) >> 30) & 1) == 1; // misc +4: IVs
        return new PartyMember(species, raw[0x54], egg, Gen3Text(raw.Slice(0x08, 10)));
    }

    /// <summary>Gen 3 numbers its Pokémon in its own order from Hoenn on (internal 277 = Geckarbor = #252).</summary>
    static int National3(int index) => index switch
    {
        >= 1 and <= 251 => index,
        >= 277 and <= 411 => HoennNational[index - 277],
        _ => 0,
    };

    static readonly short[] HoennNational =
    [
        252, 253, 254, 255, 256, 257, 258, 259, 260, 261, 262, 263, 264, 265, 266, 267, 268, 269, 270, 271, 272, 273, 274, 275,
        290, 291, 292, 276, 277, 285, 286, 327, 278, 279, 283, 284, 320, 321, 300, 301, 352, 343, 344, 299, 324, 302, 339, 340,
        370, 341, 342, 349, 350, 318, 319, 328, 329, 330, 296, 297, 309, 310, 322, 323, 363, 364, 365, 331, 332, 361, 362, 337,
        338, 298, 325, 326, 311, 312, 303, 307, 308, 333, 334, 360, 355, 356, 315, 287, 288, 289, 316, 317, 357, 293, 294, 295,
        366, 367, 368, 359, 353, 354, 336, 335, 369, 304, 305, 306, 351, 313, 314, 345, 346, 347, 348, 280, 281, 282, 371, 372,
        373, 374, 375, 376, 377, 378, 379, 382, 383, 384, 380, 381, 385, 386, 358,
    ];

    // ---------------- Gen 4 ----------------

    /// <summary>General block size and where trainer data, party and Dex sit in it.</summary>
    static (int Size, int Trainer, int Party, int Dex) Gen4Layout(SaveFamily f) => f switch
    {
        SaveFamily.DP => (0xC100, 0x64, 0x98, 0x12DC),
        SaveFamily.Pt => (0xCF2C, 0x68, 0xA0, 0x1328),
        _ => (0xF628, 0x64, 0x98, 0x12B8), // HGSS
    };

    const int Gen4Species = 493, Gen5Species = 649;

    static SaveSummary? ReadGen4(byte[] d, SaveFamily family)
    {
        var (size, trainer, party, dex) = Gen4Layout(family);

        // two copies of the general block (0x00000 / 0x40000): the valid one with the higher save counter is current.
        // Block end: … [save counter][block size][0x20060623][--][CRC16]; the CRC covers everything before the footer,
        // which is 0x14 bytes in DP/Pt and 0x10 in HGSS.
        int footerLength = family == SaveFamily.HGSS ? 0x10 : 0x14;
        int? current = null;
        uint best = 0;
        foreach (int slot in new[] { 0, 0x40000 })
        {
            int footer = slot + size - 0x14;
            if (U32(d, footer + 0x08) != size || U32(d, footer + 0x0C) != 0x20060623) continue;
            if (Crc16Ccitt(d.AsSpan(slot, size - footerLength)) != U16(d, footer + 0x12)) continue;
            uint counter = U32(d, footer + 0x04);
            if (current == null || counter > best)
            {
                current = slot;
                best = counter;
            }
        }
        if (current is not int g) return null;

        int t = g + trainer;
        var name = Gen4Text(d.AsSpan(t, 16));
        if (name.Length == 0) return null;
        int badges = BitOperations.PopCount(d[t + 0x1A]);
        if (family == SaveFamily.HGSS) badges += BitOperations.PopCount(d[t + 0x1F]); // Kanto

        int dexStart = g + dex;
        bool hasDex = U32(d, dexStart) == 0xBEEFCAFE;
        var caught = hasDex ? d.AsSpan(dexStart + 0x04, 0x40).ToArray() : new byte[0x40];
        var seen = hasDex ? d.AsSpan(dexStart + 0x44, 0x40).ToArray() : new byte[0x40];
        return new SaveSummary(family, name, U16(d, t + 0x10), U32(d, t + 0x14),
            U16(d, t + 0x22), d[t + 0x24], d[t + 0x25],
            badges, family == SaveFamily.HGSS ? 16 : 8,
            CountBits(seen, Gen4Species), CountBits(caught, Gen4Species),
            ReadParty(d, g + party, (int)Math.Min(6, U32(d, g + party - 4)), 236, gen5: false),
            new Pokedex(Gen4Species, seen, caught));
    }

    // ---------------- Gen 5 ----------------

    static SaveSummary? ReadGen5(byte[] d, SaveFamily family)
    {
        const int status = 0x19400, partyStart = 0x18E00;
        int misc = family == SaveFamily.BW ? 0x21200 : 0x21100;
        int dex = family == SaveFamily.BW ? 0x21600 : 0x21400;

        var name = Gen5Text(d.AsSpan(status + 0x04, 16));
        if (name.Length == 0) return null; // started, but never saved

        var seen = new byte[0x54];
        for (int k = 0; k < 4; k++) // seen: male / female / shiny male / shiny female
            for (int i = 0; i < seen.Length; i++) seen[i] |= d[dex + 0x5C + k * 0x54 + i];
        var caught = d.AsSpan(dex + 0x08, 0x54).ToArray();

        return new SaveSummary(family, name, U16(d, status + 0x14), U32(d, misc),
            U16(d, status + 0x24), d[status + 0x26], d[status + 0x27],
            BitOperations.PopCount(d[misc + 4]), 8,
            CountBits(seen, Gen5Species), CountBits(caught, Gen5Species),
            ReadParty(d, partyStart + 8, (int)Math.Min(6, U32(d, partyStart + 4)), 220, gen5: true),
            new Pokedex(Gen5Species, seen, caught));
    }

    // ---------------- Gen 6 / 7 (3DS) ----------------

    /// <summary>
    /// File size of "main", trainer (MyStatus), party, play time, money, Dex: caught bits, then 4 seen regions (male,
    /// female, shiny male, shiny female) right after them – in Gen 7 each seen region also has bits for the alternate
    /// forms, so it is longer than the caught one.
    /// </summary>
    static (int Size, int Status, int NameAt, int Party, int Time, int Money, int Badges, int Dex, int DexBytes, int SeenBytes, int Species) Layout3ds(SaveFamily f) => f switch
    {
        SaveFamily.XY => (0x65600, 0x14000, 0x48, 0x14200, 0x1800, 0x4208, 0x420C, 0x15008, 0x60, 0x60, 721),
        SaveFamily.ORAS => (0x76000, 0x14000, 0x48, 0x14200, 0x1800, 0x4208, 0x420C, 0x15008, 0x60, 0x60, 721),
        SaveFamily.SM => (0x6BE00, 0x1200, 0x38, 0x1400, 0x40C00, 0x4004, -1, 0x2A88, 0x68, 0x8C, 802),
        _ => (0x6CC00, 0x1400, 0x38, 0x1600, 0x41000, 0x4404, -1, 0x2C88, 0x68, 0x8C, 807), // USUM
    };

    static SaveSummary? Read3ds(byte[] d, SaveFamily family)
    {
        var l = Layout3ds(family);
        if (d.Length != l.Size) return null;
        var name = Gen5Text(d.AsSpan(l.Status + l.NameAt, 26));
        if (name.Length == 0) return null; // started, but never saved

        var seen = new byte[l.DexBytes];
        for (int k = 0; k < 4; k++) // seen: male / female / shiny male / shiny female
            for (int i = 0; i < seen.Length; i++) seen[i] |= d[l.Dex + l.DexBytes + l.SeenBytes * k + i];

        // Gen 7 shows a 6-digit ID made of both halves; Gen 6 the 16-bit one
        uint id32 = U32(d, l.Status);
        int id = family is SaveFamily.SM or SaveFamily.USUM ? (int)(id32 % 1000000) : (int)(id32 & 0xFFFF);
        var party = new List<PartyMember>();
        int count = Math.Min(6, (int)d[l.Party + 6 * 0x104]);
        for (int i = 0; i < count; i++)
            if (ReadMon6(d.AsSpan(l.Party + i * 0x104, 0x104), l.Species) is { } mon) party.Add(mon);

        var caught = d.AsSpan(l.Dex, l.DexBytes).ToArray();
        return new SaveSummary(family, name, id, U32(d, l.Money),
            U16(d, l.Time), d[l.Time + 2], d[l.Time + 3],
            l.Badges < 0 ? 0 : BitOperations.PopCount(d[l.Badges]), l.Badges < 0 ? 0 : 8, // Alola has trials, no badges
            CountBits(seen, l.Species), CountBits(caught, l.Species), party, new Pokedex(l.Species, seen, caught));
    }

    /// <summary>PK6/PK7 party entry: 4 blocks of 0x38 bytes, shuffled and encrypted with the encryption constant.</summary>
    static PartyMember? ReadMon6(ReadOnlySpan<byte> raw, int maxSpecies)
    {
        uint ec = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        ushort checksum = BinaryPrimitives.ReadUInt16LittleEndian(raw[6..]);
        var data = raw.ToArray();
        Crypt(data.AsSpan(0x08, 0xE0), ec);
        Crypt(data.AsSpan(0xE8), ec); // party-only part (level, stats)

        var order = BlockOrder[(int)((ec >> 13) & 31) % 24];
        var blocks = new byte[0xE0];
        for (int stored = 0; stored < 4; stored++)
            data.AsSpan(0x08 + stored * 0x38, 0x38).CopyTo(blocks.AsSpan((order[stored] - 'A') * 0x38, 0x38));

        ushort sum = 0;
        for (int i = 0; i < blocks.Length; i += 2) sum += BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(i));
        if (sum != checksum) return null; // empty slot or damaged

        int species = BinaryPrimitives.ReadUInt16LittleEndian(blocks);                          // 0x08
        if (species <= 0 || species > maxSpecies) return null;
        bool egg = ((BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(0x6C)) >> 30) & 1) == 1; // IVs, 0x74
        return new PartyMember(species, data[0xEC], egg, Gen5Text(blocks.AsSpan(0x38, 26)));     // nickname 0x40
    }

    // ---------------- Pokémon data ----------------

    /// <summary>Order in which the four 32-byte blocks A–D are stored, by shuffle value (PID bits 13–17 mod 24).</summary>
    static readonly string[] BlockOrder =
    [
        "ABCD", "ABDC", "ACBD", "ACDB", "ADBC", "ADCB", "BACD", "BADC", "BCAD", "BCDA", "BDAC", "BDCA",
        "CABD", "CADB", "CBAD", "CBDA", "CDAB", "CDBA", "DABC", "DACB", "DBAC", "DBCA", "DCAB", "DCBA",
    ];

    static List<PartyMember> ReadParty(byte[] d, int start, int count, int entrySize, bool gen5)
    {
        var party = new List<PartyMember>();
        for (int i = 0; i < count; i++)
        {
            int at = start + i * entrySize;
            if (at + entrySize > d.Length) break;
            if (ReadMon(d.AsSpan(at, entrySize), gen5) is { } mon) party.Add(mon);
        }
        return party;
    }

    static PartyMember? ReadMon(ReadOnlySpan<byte> raw, bool gen5)
    {
        uint pid = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        ushort checksum = BinaryPrimitives.ReadUInt16LittleEndian(raw[6..]);
        var data = raw.ToArray();
        Crypt(data.AsSpan(0x08, 0x80), checksum);            // the four data blocks
        Crypt(data.AsSpan(0x88), pid);                        // party-only part (level, stats)

        var order = BlockOrder[(int)((pid >> 13) & 31) % 24];
        var blocks = new byte[0x80];
        for (int stored = 0; stored < 4; stored++)
            data.AsSpan(0x08 + stored * 32, 32).CopyTo(blocks.AsSpan((order[stored] - 'A') * 32, 32));

        ushort sum = 0;
        for (int i = 0; i < blocks.Length; i += 2) sum += BinaryPrimitives.ReadUInt16LittleEndian(blocks.AsSpan(i));
        if (sum != checksum) return null; // empty slot or damaged

        int species = BinaryPrimitives.ReadUInt16LittleEndian(blocks);                       // block A +0x00
        if (species <= 0 || species > Gen5Species) return null;
        bool egg = ((BinaryPrimitives.ReadUInt32LittleEndian(blocks.AsSpan(0x30)) >> 30) & 1) == 1; // IVs, block B +0x10
        var nick = blocks.AsSpan(0x40, 22);                                                     // block C +0x00
        return new PartyMember(species, data[0x8C], egg, gen5 ? Gen5Text(nick) : Gen4Text(nick));
    }

    /// <summary>The games' own stream cipher: LCRNG over 16-bit words.</summary>
    static void Crypt(Span<byte> data, uint seed)
    {
        for (int i = 0; i + 1 < data.Length; i += 2)
        {
            seed = seed * 0x41C64E6D + 0x6073;
            var word = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(data[i..]) ^ (seed >> 16));
            BinaryPrimitives.WriteUInt16LittleEndian(data[i..], word);
        }
    }

    // ---------------- text ----------------

    /// <summary>
    /// Gen 3 (western games): the character of each byte; 0xFF ends the text, '·' = no letter here (Japanese-only glyphs,
    /// symbols). Generated from PKHeX's StringConverter3.
    /// </summary>
    const string Gen3Chars =
        " ÀÁÂÇÈÉÊËÌ·ÎÏÒÓÔŒÙÚÛÑßàá·çèéêëì·îïòóôœùúûñºª·&+······=;··························¿¡·······Í%()··" +
        "········â······í·····················<>··························0123456789!?.-·…“”‘'♂♀$,·/ABCDE" +
        "FGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz·:ÄÖÜäöü········";

    /// <summary>Game Boy text (western games), one character per byte, 0x50 ends it. Tables from PKHeX's StringConverter1/2.</summary>
    const string Gen1Chars =
        "································································" +
        "·····························*··················@#“”·…········· " +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ():;[]abcdefghijklmnopqrstuvwxyzàèéùÀÁ" +
        "ÄÖÜäöüÈÉÌÍÑÒÓÙÚáìíñòóúº········'’··-··?!·&%····♂¥×./,♀0123456789";

    const string Gen2Chars =
        "································································" +
        "·····························*··················@#“”·…········· " +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ():;[]abcdefghijklmnopqrstuvwxyzàèéùßç" +
        "ÄÖÜäöüëïâôûêîÙÚá················’··-··?!·&%····♂¥×./,♀0123456789";

    static string Gen12Text(ReadOnlySpan<byte> s, string chars)
    {
        var sb = new StringBuilder();
        foreach (byte b in s)
        {
            if (b == 0x50) break;
            if (chars[b] != '·') sb.Append(chars[b]);
        }
        return sb.ToString().Trim();
    }

    static string Gen3Text(ReadOnlySpan<byte> s)
    {
        var sb = new StringBuilder();
        foreach (byte b in s)
        {
            if (b == 0xFF) break;
            char ch = b < Gen3Chars.Length ? Gen3Chars[b] : '·';
            if (ch != '·') sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Gen 4 codes 0x015F–0x01E9 (accented letters, punctuation); '·' = symbol without a font character.</summary>
    const string Gen4Latin =
        "ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ·ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö·øùúûüýþÿŒœŞşªº···$¡¿!?,.··/‘'“”„«»()♂♀+-*#=&~:;··········@·%··········· ·········°_";

    static string Gen4Text(ReadOnlySpan<byte> s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < s.Length; i += 2)
        {
            int c = s[i] | s[i + 1] << 8;
            if (c == 0xFFFF) break;
            char ch = c switch
            {
                >= 0x0121 and <= 0x012A => (char)('0' + c - 0x0121),
                >= 0x012B and <= 0x0144 => (char)('A' + c - 0x012B),
                >= 0x0145 and <= 0x015E => (char)('a' + c - 0x0145),
                >= 0x015F and <= 0x01E9 => Gen4Latin[c - 0x015F],
                0x00E5 => '…',
                0x00EE => '♂',
                0x00EF => '♀',
                _ => '·',
            };
            if (ch != '·') sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Gen 5 stores UTF-16 with its own glyphs for ♂/♀.</summary>
    static string Gen5Text(ReadOnlySpan<byte> s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i + 1 < s.Length; i += 2)
        {
            int c = s[i] | s[i + 1] << 8;
            if (c is 0xFFFF or 0) break;
            sb.Append(c switch
            {
                0x246D or 0xE08E => '♂', // Gen 5 / Gen 6+ glyphs
                0x246E or 0xE08F => '♀',
                _ => (char)c,
            });
        }
        return sb.ToString().Trim();
    }

    // ---------------- helpers ----------------

    static int CountBits(ReadOnlySpan<byte> bits, int species)
    {
        int n = 0;
        for (int i = 0; i < species; i++)
            if ((bits[i >> 3] >> (i & 7) & 1) == 1) n++;
        return n;
    }

    static ushort Crc16Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int k = 0; k < 8; k++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }

    static ushort U16(byte[] d, int at) => BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(at));
    static uint U32(byte[] d, int at) => BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(at));
}
