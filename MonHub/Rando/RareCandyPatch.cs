using System.IO;

namespace RandoApp;

/// <summary>
/// Rare Candies for Game Boy and GBA games (Gen 1–3), where MonHub has no cheat file: every one of these games puts one
/// Potion into the player's PC at the start of a new game – the patch turns it into 99 Rare Candies. The spot is found by
/// the code/data shape, so it works for every language and revision; if it isn't found exactly once, nothing is changed.
/// </summary>
public static class RareCandyPatch
{
    const int Quantity = 99;

    /// <summary>Offsets of the item id and quantity bytes to change, or null.</summary>
    record Spot(int ItemAt, int QuantityAt, int ItemWidth, int NewItem);

    static readonly Dictionary<(string Path, long Size, DateTime Time), bool> Checked = new();

    /// <summary>Whether this ROM (original or run) can get the Rare Candies – cached per file version.</summary>
    public static bool CanPatch(string path, RomPlatform platform)
    {
        if (platform is not (RomPlatform.Gb or RomPlatform.Gba)) return false;
        try
        {
            var info = new FileInfo(path);
            var key = (path, info.Length, info.LastWriteTimeUtc);
            lock (Checked)
                if (Checked.TryGetValue(key, out var known)) return known;
            bool ok = Find(File.ReadAllBytes(path), platform) != null;
            lock (Checked) Checked[key] = ok;
            return ok;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Patches the ROM file; true when it worked.</summary>
    public static bool Apply(string path, RomPlatform platform)
    {
        var rom = File.ReadAllBytes(path);
        if (Find(rom, platform) is not { } spot) return false;
        rom[spot.ItemAt] = (byte)spot.NewItem;
        if (spot.ItemWidth == 2) rom[spot.ItemAt + 1] = (byte)(spot.NewItem >> 8);
        rom[spot.QuantityAt] = Quantity;
        File.WriteAllBytes(path, rom);
        return true;
    }

    static Spot? Find(byte[] rom, RomPlatform platform) => platform == RomPlatform.Gb ? FindGb(rom) : FindGba(rom);

    /// <summary>
    /// Gen 1 (OakSpeech): ld hl, wNumBoxItems / ld a, POTION ($14) / ld [wram], a / ld a, 1 / ld [wram], a / call AddItemToInventory.
    /// Gen 2 writes the PC list directly: ld hl, wNumPCItems / ld [hl], 1 / inc hl / ld [hl], POTION ($12) / inc hl / ld [hl], 1 /
    /// inc hl / ld [hl], -1. Rare Candy is $28 in Gen 1, $20 in Gen 2; the generation comes from the header title.
    /// </summary>
    static Spot? FindGb(byte[] rom)
    {
        if (rom.Length < 0x150) return null;
        var title = System.Text.Encoding.ASCII.GetString(rom, 0x134, 16).ToUpperInvariant();
        bool gen2 = new[] { "GOLD", "GLD", "SILVER", "SILBER", "SLV", "CRYSTAL", "KRISTALL" }.Any(title.Contains);

        Spot? found = null;
        for (int i = 0; i + 16 < rom.Length; i++)
        {
            Spot? spot = gen2 ? Gen2At(rom, i) : Gen1At(rom, i);
            if (spot == null) continue;
            if (found != null) return null; // not unique – leave the game alone
            found = spot;
        }
        return found;

        static bool IsWram(byte high) => high is >= 0xC0 and <= 0xDF;

        static Spot? Gen1At(byte[] rom, int i)
        {
            // 3E 14  EA lo hi  3E 01  EA lo hi – both stores into WRAM, a call right after, ld hl, wram close by
            if (rom[i] != 0x3E || rom[i + 1] != 0x14 || rom[i + 2] != 0xEA || rom[i + 5] != 0x3E || rom[i + 6] != 0x01 || rom[i + 7] != 0xEA) return null;
            if (!IsWram(rom[i + 4]) || !IsWram(rom[i + 9])) return null;
            bool call = Enumerable.Range(i + 10, 6).Any(j => rom[j] == 0xCD);
            bool list = Enumerable.Range(Math.Max(0, i - 6), 22).Any(j => j + 2 < rom.Length && rom[j] == 0x21 && IsWram(rom[j + 2]));
            return call && list ? new Spot(i + 1, i + 6, 1, 0x28) : null;
        }

        static Spot? Gen2At(byte[] rom, int i)
        {
            // 21 lo hi  36 01  23  36 12  23  36 01  23  36 FF
            ReadOnlySpan<byte> tail = [0x36, 0x01, 0x23, 0x36, 0x12, 0x23, 0x36, 0x01, 0x23, 0x36, 0xFF];
            if (rom[i] != 0x21 || !IsWram(rom[i + 2]) || !rom.AsSpan(i + 3, tail.Length).SequenceEqual(tail)) return null;
            return new Spot(i + 7, i + 10, 1, 0x20);
        }
    }

    /// <summary>
    /// Gen 3 (Ruby/Sapphire/Emerald/FireRed/LeafGreen): the new game PC item table { ITEM_POTION, 1 }, { ITEM_NONE, 0 }
    /// (u16 pairs 0D 00 01 00 00 00 00 00) – the one the game's code points to (0x08000000 + offset stored in the ROM).
    /// Rare Candy is item 68.
    /// </summary>
    static Spot? FindGba(byte[] rom)
    {
        ReadOnlySpan<byte> table = [0x0D, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00];
        var candidates = new List<int>();
        for (int i = 0; i + 8 <= rom.Length; i += 2)
            if (rom.AsSpan(i, 8).SequenceEqual(table)) candidates.Add(i);
        if (candidates.Count == 0) return null;

        var referenced = new HashSet<uint>(candidates.Select(c => 0x08000000u + (uint)c));
        var hits = new HashSet<int>();
        for (int i = 0; i + 4 <= rom.Length; i += 4)
        {
            uint value = BitConverter.ToUInt32(rom, i);
            if (referenced.Contains(value)) hits.Add((int)(value - 0x08000000u));
        }
        return hits.Count == 1 ? new Spot(hits.First(), hits.First() + 2, 2, 68) : null;
    }
}
