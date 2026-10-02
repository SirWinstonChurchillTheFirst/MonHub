using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace PokeHub;

/// <summary>
/// One set of keys, controller buttons and speeds for melonDS and mGBA, set under "Emulatoren". Written into the
/// emulators' own config files (melonDS.toml; mGBA config.ini + qt.ini) – only once the player has changed something here,
/// so settings made in the emulators themselves stay untouched until then.
/// </summary>
public class ControlSettings
{
    /// <summary>Action → Qt key code (both emulators use Qt key codes).</summary>
    public Dictionary<string, int> Keys { get; set; } = new(Defaults.Keys);
    /// <summary>Action → controller input: "b0" … "b10" (SDL button of an Xbox-style pad), "lt", "rt", "" = none.</summary>
    public Dictionary<string, string> Pad { get; set; } = new(Defaults.Pad);
    /// <summary>Speed while "Schneller" is held, and of the on/off switch (1 = normal).</summary>
    public double FastForward { get; set; } = 4;
    public double Toggle { get; set; } = 2;

    public static readonly (string Id, string Label)[] Actions =
    [
        ("A", "A"), ("B", "B"), ("X", "X (DS, 3DS)"), ("Y", "Y (DS, 3DS)"), ("L", "L"), ("R", "R"),
        ("Start", "Start"), ("Select", "Select"), ("Fast", Txt.L("Schneller (halten)", "Faster (hold)")), ("Toggle", Txt.L("Tempo an/aus", "Speed on/off")),
    ];

    /// <summary>What MonHub set up before this existed – the same as the shipped emulator settings.</summary>
    public static class Defaults
    {
        public static readonly Dictionary<string, int> Keys = new()
        {
            ["A"] = 'X', ["B"] = 'Z', ["X"] = 'S', ["Y"] = 'A', ["L"] = 'Q', ["R"] = 'W',
            ["Start"] = 0x01000004, ["Select"] = 0x01000003, ["Fast"] = 0x01000001, ["Toggle"] = ' ',
        };
        public static readonly Dictionary<string, string> Pad = new()
        {
            ["A"] = "b0", ["B"] = "b1", ["X"] = "b2", ["Y"] = "b3", ["L"] = "b4", ["R"] = "b5",
            ["Start"] = "b7", ["Select"] = "b6", ["Fast"] = "rt", ["Toggle"] = "lt",
        };
    }

    // ---------------- keyboard: WPF key → Qt key code and name ----------------

    static readonly Dictionary<Key, (int Code, string Name, string Label)> Special = new()
    {
        [Key.Space] = (' ', "Space", Txt.L("Leertaste", "Space")), [Key.Tab] = (0x01000001, "Tab", "Tab"),
        [Key.Back] = (0x01000003, "Backspace", Txt.L("Rücktaste", "Backspace")), [Key.Return] = (0x01000004, "Return", "Enter"),
        [Key.Insert] = (0x01000006, "Ins", Txt.L("Einfg", "Ins")), [Key.Delete] = (0x01000007, "Del", Txt.L("Entf", "Del")),
        [Key.Home] = (0x01000010, "Home", Txt.L("Pos1", "Home")), [Key.End] = (0x01000011, "End", Txt.L("Ende", "End")),
        [Key.PageUp] = (0x01000016, "PgUp", Txt.L("Bild ↑", "PgUp")), [Key.PageDown] = (0x01000017, "PgDown", Txt.L("Bild ↓", "PgDn")),
        [Key.LeftShift] = (0x01000020, "Shift", "Shift"), [Key.RightShift] = (0x01000020, "Shift", "Shift"),
        [Key.LeftCtrl] = (0x01000021, "Ctrl", Txt.L("Strg", "Ctrl")), [Key.RightCtrl] = (0x01000021, "Ctrl", Txt.L("Strg", "Ctrl")),
        [Key.OemComma] = (',', ",", ","), [Key.OemPeriod] = ('.', ".", "."), [Key.OemMinus] = ('-', "-", "-"), [Key.OemPlus] = ('+', "+", "+"),
    };

    /// <summary>The Qt code for a pressed key, or null for keys that can't be used (arrows = d-pad, Esc, Windows …).</summary>
    public static int? QtCode(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return 'A' + (key - Key.A);
        if (key is >= Key.D0 and <= Key.D9) return '0' + (key - Key.D0);
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return '0' + (key - Key.NumPad0);
        if (key is >= Key.F1 and <= Key.F12 && key != Key.F11) return 0x01000030 + (key - Key.F1); // F11 = full screen
        return Special.TryGetValue(key, out var s) ? s.Code : null;
    }

    /// <summary>Text for the button in MonHub.</summary>
    public static string KeyLabel(int code) =>
        code is >= 'A' and <= 'Z' or >= '0' and <= '9' ? ((char)code).ToString()
        : code is >= 0x01000030 and <= 0x0100003B ? $"F{code - 0x01000030 + 1}"
        : Special.Values.FirstOrDefault(s => s.Code == code).Label ?? "?";

    /// <summary>The name Qt's QKeySequence uses (mGBA stores shortcuts like that).</summary>
    public static string KeyName(int code) =>
        code is >= 'A' and <= 'Z' or >= '0' and <= '9' ? ((char)code).ToString()
        : code is >= 0x01000030 and <= 0x0100003B ? $"F{code - 0x01000030 + 1}"
        : Special.Values.FirstOrDefault(s => s.Code == code).Name ?? "";

    // ---------------- controller ----------------

    public static string PadLabel(string pad) => pad switch
    {
        "b0" => "A", "b1" => "B", "b2" => "X", "b3" => "Y", "b4" => "LB", "b5" => "RB", "b6" => "View", "b7" => "Menu",
        "b8" => "L-Stick", "b9" => "R-Stick", "lt" => "LT", "rt" => "RT", _ => "–",
    };

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint index, out XInputState state);

    /// <summary>Number of the first connected Xbox-style controller, or null.</summary>
    public static int? ConnectedPad()
    {
        try
        {
            for (uint i = 0; i < 4; i++)
                if (XInputGetState(i, out _) == 0) return (int)i;
        }
        catch (DllNotFoundException)
        {
        }
        return null;
    }

    /// <summary>The button/trigger held right now on any controller ("b0", "lt" …), or null. The d-pad doesn't count.</summary>
    public static string? PressedPad()
    {
        try
        {
            for (uint i = 0; i < 4; i++)
            {
                if (XInputGetState(i, out var s) != 0) continue;
                // XInput bit → SDL button number, as melonDS and mGBA see an Xbox pad
                (ushort Bit, string Id)[] map = [(0x1000, "b0"), (0x2000, "b1"), (0x4000, "b2"), (0x8000, "b3"), (0x0100, "b4"), (0x0200, "b5"),
                                                (0x0020, "b6"), (0x0010, "b7"), (0x0040, "b8"), (0x0080, "b9")];
                foreach (var (bit, id) in map)
                    if ((s.Buttons & bit) != 0) return id;
                if (s.LeftTrigger > 128) return "lt";
                if (s.RightTrigger > 128) return "rt";
            }
        }
        catch (DllNotFoundException)
        {
        }
        return null;
    }

    // ---------------- writing into the emulators ----------------

    /// <summary>melonDS: [Instance0.Keyboard] / [Instance0.Joystick] and the two speeds.</summary>
    internal void ApplyToMelonDS(List<string> lines)
    {
        (string Action, string Key)[] names = [("A", "A"), ("B", "B"), ("X", "X"), ("Y", "Y"), ("L", "L"), ("R", "R"), ("Start", "Start"),
                                              ("Select", "Select"), ("Fast", "HK_FastForward"), ("Toggle", "HK_SlowMoToggle")];
        foreach (var (action, key) in names)
        {
            HubSetup.SetToml(lines, "Instance0.Keyboard", key, Keys.GetValueOrDefault(action, -1).ToString());
            HubSetup.SetToml(lines, "Instance0.Joystick", key, MelonPad(Pad.GetValueOrDefault(action, "")).ToString());
        }
        // "Tempo an/aus" is melonDS' slow-motion switch set faster than normal
        HubSetup.SetToml(lines, null, "FastForwardFPS", (60 * FastForward).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        HubSetup.SetToml(lines, null, "SlowmoFPS", (60 * Toggle).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        HubSetup.SetToml(lines, null, "TargetFPS", "60.0");
    }

    /// <summary>melonDS joystick value: a button's number, triggers as axis 4/5 (the codes melonDS writes itself).</summary>
    static int MelonPad(string pad) => pad switch
    {
        "lt" => 0x0421FFFF,
        "rt" => 0x0521FFFF,
        _ when pad.StartsWith('b') && int.TryParse(pad.AsSpan(1), out int button) => button,
        _ => -1,
    };

    /// <summary>mGBA config.ini: keyboard ([gba.input.QT_K]), controller ([gba.input.SDLB]) and the speeds.</summary>
    internal void ApplyToMGBAConfig(List<string> lines)
    {
        foreach (var (action, key) in new[] { ("A", "keyA"), ("B", "keyB"), ("L", "keyL"), ("R", "keyR"), ("Start", "keyStart"), ("Select", "keySelect") })
        {
            HubSetup.SetIni(lines, "gba.input.QT_K", key, Keys.GetValueOrDefault(action, -1).ToString());
            var pad = Pad.GetValueOrDefault(action, "");
            var axisKey = "axis" + key[3..]; // keyL → axisL
            if (pad is "lt" or "rt")
            {
                HubSetup.SetIni(lines, "gba.input.SDLB", key, "-1");
                HubSetup.SetIni(lines, "gba.input.SDLB", axisKey + "Axis", pad == "lt" ? "+4" : "+5");
                HubSetup.SetIni(lines, "gba.input.SDLB", axisKey + "Value", "-20480");
            }
            else
            {
                HubSetup.SetIni(lines, "gba.input.SDLB", key, (pad.StartsWith('b') ? pad[1..] : "-1"));
                HubSetup.RemoveIni(lines, "gba.input.SDLB", axisKey + "Axis");
                HubSetup.RemoveIni(lines, "gba.input.SDLB", axisKey + "Value");
            }
        }
        // d-pad and left stick stay on the direction keys (GBA keys: 4 right, 5 left, 6 up, 7 down) – written out, so they
        // are there even when mGBA takes this section as the whole controller setup
        foreach (var (key, value) in new[] { ("hat0Right", "4"), ("hat0Left", "5"), ("hat0Up", "6"), ("hat0Down", "7"),
                                             ("axisRightAxis", "+0"), ("axisRightValue", "12288"), ("axisLeftAxis", "-0"), ("axisLeftValue", "-12288"),
                                             ("axisUpAxis", "-1"), ("axisUpValue", "-12288"), ("axisDownAxis", "+1"), ("axisDownValue", "12288") })
            HubSetup.SetIni(lines, "gba.input.SDLB", key, value);
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        HubSetup.SetIni(lines, "ports.qt", "fastForwardHeldRatio", FastForward.ToString(invariant));
        HubSetup.SetIni(lines, "ports.qt", "fastForwardRatio", Toggle.ToString(invariant));
    }

    static readonly (string Action, string Button)[] AzaharButtons =
        [("A", "a"), ("B", "b"), ("X", "x"), ("Y", "y"), ("L", "l"), ("R", "r"), ("Start", "start"), ("Select", "select")];

    /// <summary>
    /// Azahar qt-config.ini. Azahar binds each 3DS button to ONE input, so there are two profiles: 1 = keyboard,
    /// 2 = controller (any connected one – "maptype:all" needs no device ID). <see cref="Azahar3ds.Prepare"/> picks the
    /// controller profile when a controller is plugged in. "Tempo an/aus" is Azahar's turbo switch on both (Azahar has
    /// no hold-to-speed-up). <paramref name="keyboard"/>: also write the keyboard profile (else the player's stays).
    /// </summary>
    internal void ApplyToAzahar(List<string> lines, bool keyboard)
    {
        if (keyboard)
        {
            foreach (var (action, button) in AzaharButtons)
                if (Keys.GetValueOrDefault(action, -1) is int code and > 0)
                    Azahar3ds.Set(lines, "Controls", $@"profiles\1\button_{button}", $"\"code:{code},engine:keyboard\"");
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Toggle%20Turbo%20Mode\KeySeq", KeyName(Keys.GetValueOrDefault("Toggle", -1)));
        }
        HubSetup.SetIni(lines, "Controls", @"profiles\1\name", Txt.L("Tastatur", "Keyboard"));
        HubSetup.SetIni(lines, "Controls", @"profiles\2\name", ControllerProfile);
        foreach (var (action, button) in AzaharButtons)
            if (AzaharPad(Pad.GetValueOrDefault(action, "")) is { } param)
                Azahar3ds.Set(lines, "Controls", $@"profiles\2\button_{button}", $"\"{param}\"");
        // d-pad and left stick as on the 3DS (d-pad / circle pad)
        foreach (var (button, index) in new[] { ("up", 11), ("down", 12), ("left", 13), ("right", 14) })
            Azahar3ds.Set(lines, "Controls", $@"profiles\2\button_{button}", $"\"{AllControllers},button:{index}\"");
        Azahar3ds.Set(lines, "Controls", @"profiles\2\circle_pad", $"\"{AllControllers},axis_x:0,axis_y:1,deadzone:0.100000\"");
        HubSetup.SetIni(lines, "Controls", @"profiles\size", "2");
        if (AzaharPad(Pad.GetValueOrDefault("Toggle", "")) is { } turbo)
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Toggle%20Turbo%20Mode\controller_keyseq", $"\"{turbo}\"");
        Azahar3ds.Set(lines, "Renderer", "turbo_limit", Math.Round(Toggle * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal const string ControllerProfile = "Controller"; // plain ASCII: Azahar writes it back unchanged
    const string AllControllers = "api:controller,engine:sdl,guid:0,maptype:all,port:0";

    /// <summary>A hub controller input ("b0" = SDL joystick button of an Xbox pad, "lt") as Azahar controller-API parameters.</summary>
    static string? AzaharPad(string pad) => pad switch
    {
        "lt" => $"{AllControllers},axis:4,direction:+,threshold:0.500000",
        "rt" => $"{AllControllers},axis:5,direction:+,threshold:0.500000",
        // joystick numbering (A B X Y LB RB View Menu LS RS) → SDL game controller buttons
        _ when pad.StartsWith('b') && int.TryParse(pad.AsSpan(1), out int b) && b is >= 0 and <= 9
            => $"{AllControllers},button:{new[] { 0, 1, 2, 3, 9, 10, 4, 6, 7, 8 }[b]}",
        _ => null,
    };

    [DllImport("winmm.dll")]
    static extern int joyGetPosEx(int id, ref JoyInfoEx info);

    [StructLayout(LayoutKind.Sequential)]
    struct JoyInfoEx { public int Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2; }

    /// <summary>Any controller plugged in (Xbox, PlayStation, Switch …) – decides Azahar's input profile.</summary>
    public static bool AnyPadConnected()
    {
        if (ConnectedPad() != null) return true;
        try
        {
            for (int id = 0; id < 16; id++)
            {
                var info = new JoyInfoEx { Size = Marshal.SizeOf<JoyInfoEx>(), Flags = 0xFF };
                if (joyGetPosEx(id, ref info) == 0) return true;
            }
        }
        catch (DllNotFoundException)
        {
        }
        return false;
    }

    /// <summary>mGBA qt.ini: the two speed shortcuts on keyboard and controller.</summary>
    internal void ApplyToMGBAShortcuts(List<string> lines)
    {
        foreach (var (action, name) in new[] { ("Fast", "holdFastForward"), ("Toggle", "fastForward") })
        {
            HubSetup.SetIni(lines, "shortcutKey", name, KeyName(Keys.GetValueOrDefault(action, -1)));
            var pad = Pad.GetValueOrDefault(action, "");
            HubSetup.SetIni(lines, "shortcutButton", name, pad.StartsWith('b') ? pad[1..] : "-1");
            if (pad is "lt" or "rt") HubSetup.SetIni(lines, "shortcutAxis", name, pad == "lt" ? "+4" : "+5");
            else HubSetup.RemoveIni(lines, "shortcutAxis", name);
        }
    }
}
