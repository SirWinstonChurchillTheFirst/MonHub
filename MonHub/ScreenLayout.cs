using System.IO;

namespace MonHub;

/// <summary>How the two screens of a DS or 3DS sit in the picture (set under "Emulatoren", used for games inside MonHub).</summary>
public enum ScreenArrangement
{
    /// <summary>Whatever leaves the least empty space in MonHub's game area when the game starts.</summary>
    Auto,
    /// <summary>Top screen above the bottom one.</summary>
    Stacked,
    /// <summary>Both screens next to each other.</summary>
    SideBySide,
    /// <summary>The top screen big, the bottom one small next to it.</summary>
    BigAndSmall,
}

/// <summary>
/// Screen arrangements for games inside MonHub: written into the emulator's settings right before it starts, and the
/// picture's proportions, so MonHub can give the window exactly the room the picture needs (no black bars around it).
/// </summary>
public static class ScreenLayout
{
    /// <summary>Width to height of the picture with these screens (null: the emulator decides, e.g. mGBA's own size).</summary>
    public static double Ratio(bool threeDs, ScreenArrangement arrangement) => (threeDs, arrangement) switch
    {
        (false, ScreenArrangement.SideBySide) => 512.0 / 192,
        (false, ScreenArrangement.BigAndSmall) => 2.0,           // melonDS side by side, top screen emphasized: big, the touch screen small next to it
        (false, _) => 256.0 / 384,
        (true, ScreenArrangement.SideBySide) => 720.0 / 240,     // 400 + 320 wide
        (true, ScreenArrangement.BigAndSmall) => 480.0 / 240,    // Azahar "large screen": top 4× the bottom one
        (true, _) => 400.0 / 480,
    };

    /// <summary>
    /// "Automatisch": of the two arrangements with equally big screens, the one that fills the game area best (its width to
    /// height). "Groß + klein" is never picked by itself – there the touch screen gets small.
    /// </summary>
    public static ScreenArrangement Resolve(ScreenArrangement chosen, bool threeDs, double area)
    {
        if (chosen != ScreenArrangement.Auto) return chosen;
        if (area <= 0 || double.IsNaN(area)) area = 16.0 / 10;
        double Fill(double ratio) => ratio < area ? ratio / area : area / ratio;
        return Fill(Ratio(threeDs, ScreenArrangement.SideBySide)) > Fill(Ratio(threeDs, ScreenArrangement.Stacked))
            ? ScreenArrangement.SideBySide : ScreenArrangement.Stacked;
    }

    /// <summary>
    /// The arrangement into the emulator's settings (it reads them when it starts; it isn't running yet). DeSmuME has no
    /// "big and small" – there it is side by side.
    /// </summary>
    public static void Apply(string? emulator, ScreenArrangement arrangement)
    {
        try
        {
            if (emulator == GameLibrary.MelonDS && File.Exists(HubPaths.MelonDSToml))
            {
                var lines = File.ReadAllLines(HubPaths.MelonDSToml).ToList();
                // 1 vertical, 2 horizontal; "big and small" is horizontal with the top screen emphasized (sizing 1) – not
                // melonDS' "hybrid", which shows the top screen a second time; no gap, upright
                HubSetup.SetToml(lines, "Instance0.Window0", "ScreenLayout", arrangement is ScreenArrangement.Stacked ? "1" : "2");
                HubSetup.SetToml(lines, "Instance0.Window0", "ScreenSizing", arrangement is ScreenArrangement.BigAndSmall ? "1" : "0");
                HubSetup.SetToml(lines, "Instance0.Window0", "ScreenGap", "0");
                HubSetup.SetToml(lines, "Instance0.Window0", "ScreenRotation", "0");
                SafeFile.WriteAllLines(HubPaths.MelonDSToml, lines);
            }
            else if (emulator == GameLibrary.DeSmuME && HubPaths.DeSmuMEExe != null)
            {
                var ini = Path.Combine(HubPaths.DeSmuME, "desmume.ini");
                var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
                HubSetup.SetIni(lines, "Video", "LCDsLayout", arrangement == ScreenArrangement.Stacked ? "0" : "1");
                HubSetup.SetIni(lines, "Video", "Window Rotate", "0");
                SafeFile.WriteAllLines(ini, lines);
            }
            else if (emulator == GameLibrary.Azahar)
            {
                // 0 top above bottom, 2 large screen, 3 side by side
                Azahar3ds.SetLayout(arrangement switch
                {
                    ScreenArrangement.SideBySide => "3",
                    ScreenArrangement.BigAndSmall => "2",
                    _ => "0",
                });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // then the emulator keeps its last arrangement – the game starts all the same
        }
    }

    /// <summary>Width to height of a Game Boy / GBA picture in mGBA.</summary>
    public static double GameBoyRatio(string rom)
    {
        var ext = Path.GetExtension(rom).ToLowerInvariant();
        if (ext == ".gba") return 240.0 / 160;
        try
        {
            using var file = File.OpenRead(rom);
            var header = new byte[0x150];
            if (file.Read(header, 0, header.Length) == header.Length
                && (header[0x143] & 0x80) == 0 && header[0x146] == 0x03)
                return 256.0 / 224; // a Super Game Boy game without colour: mGBA shows it with its SGB border
        }
        catch (IOException)
        {
        }
        return 160.0 / 144;
    }
}
