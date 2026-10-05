using MonHub;

namespace MonHub.Tests;

/// <summary>What the controls page writes into melonDS, mGBA and Azahar.</summary>
public class ControlsTests
{
    static string Value(List<string> lines, string key) =>
        lines.Last(l => l.StartsWith(key + "=") || l.StartsWith(key + " = ")).Split('=', 2)[1].Trim();

    [Fact]
    public void Defaults_CoverEveryInput()
    {
        var c = new ControlSettings();
        foreach (var (id, _) in ControlSettings.Actions)
        {
            Assert.True(c.Keys.ContainsKey(id), id);
            Assert.True(c.Pad.ContainsKey(id), id);
        }
    }

    [Theory]
    [InlineData(Avalonia.Input.Key.Up, 0x01000013)]
    [InlineData(Avalonia.Input.Key.Right, 0x01000014)]
    [InlineData(Avalonia.Input.Key.K, 'K')]
    [InlineData(Avalonia.Input.Key.Home, 0x01000010)]
    public void Keys_IncludingArrows_CanBeBound(Avalonia.Input.Key key, int qt) => Assert.Equal(qt, ControlSettings.QtCode(key));

    [Fact]
    public void Escape_IsNeverBound() => Assert.Null(ControlSettings.QtCode(Avalonia.Input.Key.Escape));

    [Fact]
    public void OldSettings_GetTheNewInputs()
    {
        // saved by 2.6: only buttons and speeds
        var c = new ControlSettings { Keys = new() { ["A"] = 'K' }, Pad = new() { ["A"] = "b1" } };
        c.FillMissing();
        Assert.Equal('K', c.Keys["A"]); // the player's choice stays
        Assert.Equal(0x01000013, c.Keys["MoveUp"]);
        Assert.Equal("du", c.Pad["PadUp"]);
    }

    [Fact]
    public void MelonDS_Defaults_StickAndDpadBothMove()
    {
        var lines = new List<string> { "[Instance0.Keyboard]", "[Instance0.Joystick]" };
        new ControlSettings().ApplyToMelonDS(lines);
        // up: d-pad (hat 0, up) plus the left stick up (axis 1, negative) – the same stick code melonDS shipped with
        Assert.Equal((0x0111FFFF & ~0xFFFF | 0x101).ToString(), Value(lines, "Up"));
        Assert.Equal((0x0101FFFF & ~0xFFFF | 0x104).ToString(), Value(lines, "Down"));
        Assert.Contains($"Up = {0x01000013}", lines); // keyboard: arrow up
    }

    [Fact]
    public void Azahar_Defaults_LeftStickIsCirclePad_DpadIsDpad()
    {
        var lines = new List<string>();
        new ControlSettings().ApplyToAzahar(lines, keyboard: true);
        Assert.Contains(@"profiles\2\circle_pad=""api:controller,engine:sdl,guid:0,maptype:all,port:0,axis_x:0,axis_y:1,deadzone:0.100000""", lines);
        Assert.Contains(@"profiles\2\button_up=""api:controller,engine:sdl,guid:0,maptype:all,port:0,button:11""", lines);
        // keyboard: arrows = circle pad (D = half way), T = d-pad up
        var circle = Value(lines, @"profiles\1\circle_pad");
        Assert.Contains("engine:analog_from_button", circle);
        Assert.Contains("up:code$016777235$1engine$0keyboard", circle);
        Assert.Contains("modifier:code$068$1engine$0keyboard", circle);
        Assert.Equal(@"""code:84,engine:keyboard""", Value(lines, @"profiles\1\button_up"));
        Assert.Equal(@"""code:49,engine:keyboard""", Value(lines, @"profiles\1\button_zl"));
    }

    [Fact]
    public void Azahar_DpadOnMove_WalksWithTheDpad()
    {
        // the player puts the controller's d-pad on "Move" (no roller skates in X/Y) and empties the 3DS d-pad
        var c = new ControlSettings();
        foreach (var (dir, pad) in new[] { ("Up", "du"), ("Down", "dd"), ("Left", "dl"), ("Right", "dr") })
        {
            c.Pad["Move" + dir] = pad;
            c.Pad["Pad" + dir] = "";
        }
        var lines = new List<string>();
        c.ApplyToAzahar(lines, keyboard: false);
        var circle = Value(lines, @"profiles\2\circle_pad");
        Assert.Contains("engine:analog_from_button", circle);
        Assert.Contains("up:api$0controller$1engine$0sdl$1guid$00$1maptype$0all$1port$00$1button$011", circle);
        Assert.Equal(@"""code:0,engine:keyboard""", Value(lines, @"profiles\2\button_up")); // emptied: a key that doesn't exist
    }

    [Fact]
    public void Azahar_StickOnDpad_IsNotHeldAtRest()
    {
        // the 3DS d-pad on the right stick: Azahar takes "-" as "pressed BELOW the threshold", so up and left need
        // a negative one – with +0.5 the resting stick (0) held them down for good
        var c = new ControlSettings();
        foreach (var (dir, pad) in new[] { ("Up", "rsu"), ("Down", "rsd"), ("Left", "rsl"), ("Right", "rsr") })
            c.Pad["Pad" + dir] = pad;
        var lines = new List<string>();
        c.ApplyToAzahar(lines, keyboard: false);
        Assert.EndsWith(@"axis:3,direction:-,threshold:-0.500000""", Value(lines, @"profiles\2\button_up"));
        Assert.EndsWith(@"axis:3,direction:+,threshold:0.500000""", Value(lines, @"profiles\2\button_down"));
        Assert.EndsWith(@"axis:2,direction:-,threshold:-0.500000""", Value(lines, @"profiles\2\button_left"));
        Assert.EndsWith(@"axis:2,direction:+,threshold:0.500000""", Value(lines, @"profiles\2\button_right"));
        // every "-" direction MonHub writes for Azahar has a negative threshold
        Assert.DoesNotContain(lines, l => l.Contains("direction:-,threshold:0.5") || l.Contains("direction$0-$1threshold$00.5"));
    }

    [Fact]
    public void Azahar_StickDirectionsOnMove_AreNotHeldAtRest()
    {
        // "Move" from single stick directions in another order than a whole stick: built from four inputs
        var c = new ControlSettings();
        c.Pad["MoveUp"] = "rsu"; c.Pad["MoveDown"] = "lsd"; c.Pad["MoveLeft"] = "lsl"; c.Pad["MoveRight"] = "lsr";
        var lines = new List<string>();
        c.ApplyToAzahar(lines, keyboard: false);
        var circle = Value(lines, @"profiles\2\circle_pad");
        Assert.Contains("engine:analog_from_button", circle);
        Assert.Contains("axis$03$1direction$0-$1threshold$0-0.500000", circle);  // up: right stick, negative
        Assert.Contains("axis$00$1direction$0-$1threshold$0-0.500000", circle);  // left: left stick, negative
        Assert.Contains("axis$01$1direction$0+$1threshold$00.500000", circle);   // down: positive
    }

    [Fact]
    public void MGBA_DirectionsFromMoveAndDpad()
    {
        var lines = new List<string> { "[gba.input.QT_K]", "[gba.input.SDLB]" };
        new ControlSettings().ApplyToMGBAConfig(lines);
        Assert.Contains("hat0Up=6", lines);
        Assert.Contains("axisUpAxis=-1", lines);
        Assert.Contains("axisRightAxis=+0", lines);
        Assert.Contains($"keyUp={0x01000013}", lines);
    }

    [Fact]
    public void SixtyFps_IsOff_AndTheOldDefaultIsLeftBehind()
    {
        Assert.False(new HubConfig().AzaharSixtyFps);
        var old = System.Text.Json.JsonSerializer.Deserialize<HubConfig>("{\"Azahar60Fps\": true}")!;
        Assert.False(old.AzaharSixtyFps);
    }

    [Fact]
    public void MGBA_TwoSticks_MoveStickWins()
    {
        var c = new ControlSettings();
        foreach (var (dir, d) in new[] { ("Up", "u"), ("Down", "d"), ("Left", "l"), ("Right", "r") })
        {
            c.Pad["Move" + dir] = "ls" + d;
            c.Pad["Pad" + dir] = "rs" + d;
        }
        var lines = new List<string> { "[gba.input.QT_K]", "[gba.input.SDLB]" };
        c.ApplyToMGBAConfig(lines);
        Assert.Contains("axisUpAxis=-1", lines);
        Assert.Contains("axisRightAxis=+0", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("axisUpAxis=-3") || l.StartsWith("axisRightAxis=+2"));
    }
}
