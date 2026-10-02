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
    [InlineData(System.Windows.Input.Key.Up, 0x01000013)]
    [InlineData(System.Windows.Input.Key.Right, 0x01000014)]
    [InlineData(System.Windows.Input.Key.K, 'K')]
    [InlineData(System.Windows.Input.Key.Home, 0x01000010)]
    public void Keys_IncludingArrows_CanBeBound(System.Windows.Input.Key key, int qt) => Assert.Equal(qt, ControlSettings.QtCode(key));

    [Fact]
    public void Escape_IsNeverBound() => Assert.Null(ControlSettings.QtCode(System.Windows.Input.Key.Escape));

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
    public void MGBA_DirectionsFromMoveAndDpad()
    {
        var lines = new List<string> { "[gba.input.QT_K]", "[gba.input.SDLB]" };
        new ControlSettings().ApplyToMGBAConfig(lines);
        Assert.Contains("hat0Up=6", lines);
        Assert.Contains("axisUpAxis=-1", lines);
        Assert.Contains("axisRightAxis=+0", lines);
        Assert.Contains($"keyUp={0x01000013}", lines);
    }
}
