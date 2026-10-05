using MonHub;

namespace MonHub.Tests;

/// <summary>
/// The save reader against saves whose content is known (expected values checked with PKHeX when the fixtures were made).
/// </summary>
public class SaveInspectorTests
{
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory]
    [InlineData("BPGD", SaveFamily.FRLG)] // Blattgrün
    [InlineData("BPRE", SaveFamily.FRLG)]
    [InlineData("BPED", SaveFamily.E)]
    [InlineData("AXVE", SaveFamily.RS)]
    [InlineData("AXPD", SaveFamily.RS)]
    [InlineData("CPUD", SaveFamily.Pt)]
    [InlineData("IRED", SaveFamily.B2W2)]
    [InlineData("A2BA", SaveFamily.USUM)]
    public void FamilyOf_KnowsTheGames(string code, SaveFamily family) => Assert.Equal(family, SaveInspector.FamilyOf(code));

    [Theory]
    [InlineData("")]
    [InlineData("AB")]
    [InlineData("ZZZZ")]
    public void FamilyOf_UnknownCode_IsNull(string code) => Assert.Null(SaveInspector.FamilyOf(code));

    // ---------- GBA: real saves, saved at the first chance ----------

    [Theory]
    [InlineData("LG_ABBA.sav", SaveFamily.FRLG, "ABBA", 15503)]
    [InlineData("EM_Ida.sav", SaveFamily.E, "Ida", 30513)]
    [InlineData("RU_SETH.sav", SaveFamily.RS, "SETH", 63068)]
    public void Gen3_FreshSave(string file, SaveFamily family, string trainer, int id)
    {
        var s = SaveInspector.Read(Fixture(file), family);
        Assert.NotNull(s);
        Assert.Equal(trainer, s.Trainer);
        Assert.Equal(id, s.TrainerId);
        Assert.Equal(3000u, s.Money); // what every game starts with (FRLG/E store it XORed with the security key)
        Assert.Equal(0, s.Badges);
        Assert.Empty(s.Party);
        Assert.Equal(386, s.Dex.Size);
        Assert.Equal(0, s.DexCaught);
    }

    /// <summary>The same saves with Pokédex, money, badges and a team set by PKHeX – Hoenn Pokémon have own internal numbers in Gen 3.</summary>
    [Theory]
    [InlineData("LG_ABBA_filled.sav", SaveFamily.FRLG)]
    [InlineData("EM_Ida_filled.sav", SaveFamily.E)]
    [InlineData("RU_SETH_filled.sav", SaveFamily.RS)]
    public void Gen3_FilledSave(string file, SaveFamily family)
    {
        var s = SaveInspector.Read(Fixture(file), family);
        Assert.NotNull(s);
        Assert.Equal(123456u, s.Money);
        Assert.Equal(5, s.Badges);
        Assert.Equal(243, s.DexSeen);
        Assert.Equal(117, s.DexCaught);
        Assert.Equal([25, 280, 384, 252, 151, 386], s.Party.Select(p => p.Species));
        Assert.Equal([5, 14, 23, 32, 41, 50], s.Party.Select(p => p.Level));
        Assert.True(s.Party[3].IsEgg);
        Assert.Equal("Kiki", s.Party[1].Nickname);
        // the Pokédex flags agree with the counts, and a caught Pokémon is always seen
        Assert.Equal(117, Enumerable.Range(1, 386).Count(s.Dex.Caught));
        Assert.All(Enumerable.Range(1, 386).Where(s.Dex.Caught), n => Assert.True(s.Dex.Seen(n)));
    }

    [Fact]
    public void Gen3_DamagedNewestSlot_FallsBackOrFailsQuietly()
    {
        var data = File.ReadAllBytes(Fixture("LG_ABBA_filled.sav"));
        // break every section of both slots: nothing valid is left – no exception, no summary
        for (int slot = 0; slot < 2; slot++)
            for (int i = 0; i < 14; i++)
                data[slot * 0xE000 + i * 0x1000 + 0x10] ^= 0xFF;
        var path = Path.Combine(Path.GetTempPath(), $"monhub-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Null(SaveInspector.Read(path, SaveFamily.FRLG));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(SaveFamily.FRLG, 0)]
    [InlineData(SaveFamily.FRLG, 100)]
    [InlineData(SaveFamily.Pt, 0x1000)]
    [InlineData(SaveFamily.XY, 0x1000)]
    public void TruncatedOrEmptyFile_IsNull(SaveFamily family, int length)
    {
        var path = Path.Combine(Path.GetTempPath(), $"monhub-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, new byte[length]);
            Assert.Null(SaveInspector.Read(path, family));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFile_IsNull() => Assert.Null(SaveInspector.Read(Fixture("does-not-exist.sav"), SaveFamily.E));

    [Fact]
    public void HugeFile_IsNotRead()
    {
        // a big file that only happens to be named like a save is skipped, not loaded into memory
        var path = Path.Combine(Path.GetTempPath(), $"huge-{Guid.NewGuid():N}.sav");
        using (var f = File.Create(path)) f.SetLength(5 * 1024 * 1024);
        try { Assert.Null(SaveInspector.Read(path, SaveFamily.SM)); }
        finally { File.Delete(path); }
    }

    // ---------- Game Boy: Rot (ASH) and Kristall (GERT) saved at the first chance, filled copies by PKHeX ----------

    [Theory]
    [InlineData("POKEMON RED", SaveFamily.RBY)]
    [InlineData("POKEMON BLUE", SaveFamily.RBY)]
    [InlineData("POKEMON YEL", SaveFamily.RBY)]       // German Gelb: the title is cut by its product code
    [InlineData("POKEMON_GLD", SaveFamily.GS)]
    [InlineData("POKEMON_SLV", SaveFamily.GS)]
    [InlineData("PM_CRYSTAL", SaveFamily.C)]
    public void FamilyOfTitle_KnowsTheGameBoyGames(string title, SaveFamily family) => Assert.Equal(family, SaveInspector.FamilyOfTitle(title));

    [Fact]
    public void FamilyOfTitle_Unknown_IsNull() => Assert.Null(SaveInspector.FamilyOfTitle("TETRIS"));

    [Theory]
    [InlineData("RO_ASH.sav", SaveFamily.RBY, "ASH", 59170, 8)]
    [InlineData("KR_GERT.sav", SaveFamily.C, "GERT", 57942, 16)]   // 32 KB + the clock's 48 bytes
    public void GameBoy_FreshSave(string file, SaveFamily family, string trainer, int id, int badgeSlots)
    {
        var s = SaveInspector.Read(Fixture(file), family);
        Assert.NotNull(s);
        Assert.Equal(trainer, s.Trainer);
        Assert.Equal(id, s.TrainerId);
        Assert.Equal(3000u, s.Money);
        Assert.Equal(badgeSlots, s.BadgeSlots);
        Assert.Empty(s.Party);
    }

    [Theory]
    [InlineData("RO_ASH_filled.sav", SaveFamily.RBY, 151, 101, 55, 5, new[] { 25, 150, 1, 151, 112, 6 }, -1)]
    [InlineData("KR_GERT_filled.sav", SaveFamily.C, 251, 165, 82, 10, new[] { 25, 249, 152, 251, 243, 175 }, 5)]
    [InlineData("GD_filled.sav", SaveFamily.GS, 251, 165, 82, 10, new[] { 25, 249, 152, 251, 243, 175 }, 5)]
    public void GameBoy_FilledSave(string file, SaveFamily family, int size, int seen, int caught, int badges, int[] team, int egg)
    {
        var s = SaveInspector.Read(Fixture(file), family);
        Assert.NotNull(s);
        Assert.Equal(size, s.Dex.Size);
        Assert.Equal(seen, s.DexSeen);
        Assert.Equal(caught, s.DexCaught);
        Assert.Equal(123456u, s.Money);
        Assert.Equal(badges, s.Badges);
        Assert.Equal((123, 45, 6), (s.Hours, s.Minutes, s.Seconds)); // Gen 2 keeps the hours in 2 bytes
        Assert.Equal(team, s.Party.Select(p => p.Species));          // Gen 1: from its own internal numbers
        Assert.Equal("KIKI", s.Party[1].Nickname);
        for (int i = 0; i < s.Party.Count; i++) Assert.Equal(i == egg, s.Party[i].IsEgg);
    }

    [Fact]
    public void GameBoy_WrongChecksum_IsNull()
    {
        var data = File.ReadAllBytes(Fixture("RO_ASH_filled.sav"));
        data[0x2600] ^= 0xFF; // inside the checksummed area
        var path = Path.Combine(Path.GetTempPath(), $"monhub-test-{Guid.NewGuid():N}.sav");
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Null(SaveInspector.Read(path, SaveFamily.RBY));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------- 3DS: Dex flags set by PKHeX ----------

    /// <summary>Gen 7's seen flags are longer than its caught flags (alternate forms) – reading them 0x68 apart was wrong.</summary>
    [Theory]
    [InlineData("3ds_X.sav", SaveFamily.XY, 721, 465, 217)]
    [InlineData("3ds_OR.sav", SaveFamily.ORAS, 721, 465, 217)]
    [InlineData("3ds_SN.sav", SaveFamily.SM, 802, 510, 230)]
    [InlineData("3ds_US.sav", SaveFamily.USUM, 807, 513, 232)]
    public void Gen6And7_Dex(string file, SaveFamily family, int size, int seen, int caught)
    {
        var s = SaveInspector.Read(Fixture(file), family);
        Assert.NotNull(s);
        Assert.Equal(size, s.Dex.Size);
        Assert.Equal(seen, s.DexSeen);
        Assert.Equal(caught, s.DexCaught);
        Assert.Equal(seen, Enumerable.Range(1, size).Count(s.Dex.Seen));
    }

    [Fact]
    public void Dex_OutOfRange_IsFalse()
    {
        var dex = new DexState(386, new byte[0x34], Enumerable.Repeat((byte)0xFF, 0x34).ToArray());
        Assert.False(dex.Caught(0));
        Assert.False(dex.Caught(387));
        Assert.False(dex.Seen(-5));
        Assert.True(dex.Seen(386)); // caught counts as seen
    }
}
