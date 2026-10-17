using MonHub;
using Xunit;

public class ScreenLayoutTests
{
    [Theory]
    [InlineData(false, 1.9, ScreenArrangement.SideBySide)] // a wide game area: DS screens next to each other
    [InlineData(false, 1.5, ScreenArrangement.SideBySide)]
    [InlineData(false, 1.2, ScreenArrangement.Stacked)]     // the switch is at about 4:3
    [InlineData(false, 0.9, ScreenArrangement.Stacked)]    // a tall one: above each other
    [InlineData(true, 1.5, ScreenArrangement.Stacked)]     // 3DS: side by side is very wide (3:1)
    [InlineData(true, 2.6, ScreenArrangement.SideBySide)]
    public void Auto_PicksTheArrangementWithTheLeastBorder(bool threeDs, double area, ScreenArrangement expected) =>
        Assert.Equal(expected, ScreenLayout.Resolve(ScreenArrangement.Auto, threeDs, area));

    [Fact]
    public void Auto_NeverPicksBigAndSmall()
    {
        for (double area = 0.3; area < 5; area += 0.1)
            Assert.NotEqual(ScreenArrangement.BigAndSmall, ScreenLayout.Resolve(ScreenArrangement.Auto, false, area));
    }

    [Fact]
    public void AChosenArrangement_StaysAsItIs() =>
        Assert.Equal(ScreenArrangement.BigAndSmall, ScreenLayout.Resolve(ScreenArrangement.BigAndSmall, true, 0.5));

    [Fact]
    public void Ratios_AreThePictures()
    {
        Assert.Equal(256.0 / 384, ScreenLayout.Ratio(false, ScreenArrangement.Stacked), 3);
        Assert.Equal(512.0 / 192, ScreenLayout.Ratio(false, ScreenArrangement.SideBySide), 3);
        Assert.Equal(400.0 / 480, ScreenLayout.Ratio(true, ScreenArrangement.Stacked), 3);
        Assert.Equal(3.0, ScreenLayout.Ratio(true, ScreenArrangement.SideBySide), 3);
    }

    [Fact]
    public void GameBoyRatio_ByFileAndHeader()
    {
        var dir = Path.Combine(Path.GetTempPath(), "monhub-ratio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(1.5, ScreenLayout.GameBoyRatio(Path.Combine(dir, "a.gba")), 3);
            var sgb = new byte[0x150];
            sgb[0x146] = 0x03; // Super Game Boy functions, no colour
            File.WriteAllBytes(Path.Combine(dir, "sgb.gb"), sgb);
            Assert.Equal(256.0 / 224, ScreenLayout.GameBoyRatio(Path.Combine(dir, "sgb.gb")), 3);
            var color = (byte[])sgb.Clone();
            color[0x143] = 0x80; // also for the Game Boy Color: mGBA runs it in colour, without the border
            File.WriteAllBytes(Path.Combine(dir, "color.gb"), color);
            Assert.Equal(160.0 / 144, ScreenLayout.GameBoyRatio(Path.Combine(dir, "color.gb")), 3);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
