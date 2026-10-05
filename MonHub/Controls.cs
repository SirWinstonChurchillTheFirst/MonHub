using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MonHub;

/// <summary>
/// One set of keys, controller inputs and speeds for melonDS, mGBA and Azahar, set under "Emulatoren" – every input of
/// the 3DS, so it covers the DS and the GBA too. Written into the emulators' own config files (melonDS.toml; mGBA
/// config.ini + qt.ini; Azahar qt-config.ini) – melonDS and mGBA only once the player has changed something here, so
/// settings made in the emulators themselves stay untouched until then.
///
/// Directions come twice, like on the 3DS: "Move" is the circle pad there and the d-pad on the DS/GBA; "D-pad" is the
/// 3DS's own d-pad (in X/Y: the roller skates) – on the DS/GBA a controller's d-pad there moves too.
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
        ("ZL", "ZL (3DS)"), ("ZR", "ZR (3DS)"), ("Start", "Start"), ("Select", "Select"),
        ("MoveUp", Txt.L("Laufen ↑", "Move ↑")), ("MoveDown", Txt.L("Laufen ↓", "Move ↓")),
        ("MoveLeft", Txt.L("Laufen ←", "Move ←")), ("MoveRight", Txt.L("Laufen →", "Move →")),
        ("PadUp", Txt.L("Steuerkreuz ↑ (3DS)", "D-pad ↑ (3DS)")), ("PadDown", Txt.L("Steuerkreuz ↓ (3DS)", "D-pad ↓ (3DS)")),
        ("PadLeft", Txt.L("Steuerkreuz ← (3DS)", "D-pad ← (3DS)")), ("PadRight", Txt.L("Steuerkreuz → (3DS)", "D-pad → (3DS)")),
        ("Fast", Txt.L("Schneller (halten)", "Faster (hold)")), ("Toggle", Txt.L("Tempo an/aus", "Speed on/off")),
    ];

    static readonly string[] Directions = ["Up", "Down", "Left", "Right"];

    /// <summary>What MonHub set up before this existed – the same as the shipped emulator settings.</summary>
    public static class Defaults
    {
        public static readonly Dictionary<string, int> Keys = new()
        {
            ["A"] = 'X', ["B"] = 'Z', ["X"] = 'S', ["Y"] = 'A', ["L"] = 'Q', ["R"] = 'W', ["ZL"] = '1', ["ZR"] = '2',
            ["Start"] = 0x01000004, ["Select"] = 0x01000003, ["Fast"] = 0x01000001, ["Toggle"] = ' ',
            // arrows move; the 3DS d-pad sits on T F G H, as in Azahar itself
            ["MoveUp"] = 0x01000013, ["MoveDown"] = 0x01000015, ["MoveLeft"] = 0x01000012, ["MoveRight"] = 0x01000014,
            ["PadUp"] = 'T', ["PadDown"] = 'G', ["PadLeft"] = 'F', ["PadRight"] = 'H',
        };
        public static readonly Dictionary<string, string> Pad = new()
        {
            ["A"] = "b0", ["B"] = "b1", ["X"] = "b2", ["Y"] = "b3", ["L"] = "b4", ["R"] = "b5", ["ZL"] = "", ["ZR"] = "",
            ["Start"] = "b7", ["Select"] = "b6", ["Fast"] = "rt", ["Toggle"] = "lt",
            // as on the 3DS: the left stick is the circle pad, the d-pad the d-pad
            ["MoveUp"] = "lsu", ["MoveDown"] = "lsd", ["MoveLeft"] = "lsl", ["MoveRight"] = "lsr",
            ["PadUp"] = "du", ["PadDown"] = "dd", ["PadLeft"] = "dl", ["PadRight"] = "dr",
        };
    }

    /// <summary>Settings saved before an input existed get its default (2.7 added ZL/ZR and the directions).</summary>
    public void FillMissing()
    {
        foreach (var (id, key) in Defaults.Keys) Keys.TryAdd(id, key);
        foreach (var (id, pad) in Defaults.Pad) Pad.TryAdd(id, pad);
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
        [Key.Up] = (0x01000013, "Up", "↑"), [Key.Down] = (0x01000015, "Down", "↓"), [Key.Left] = (0x01000012, "Left", "←"), [Key.Right] = (0x01000014, "Right", "→"),
    };

    /// <summary>The Qt code for a pressed key, or null for keys that can't be used (Esc, F11, Windows …).</summary>
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
        code <= 0 ? "–"
        : code is >= 'A' and <= 'Z' or >= '0' and <= '9' ? ((char)code).ToString()
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
        "b8" => "L3", "b9" => "R3", "lt" => "LT", "rt" => "RT",
        "du" => Txt.L("Kreuz ↑", "D-pad ↑"), "dd" => Txt.L("Kreuz ↓", "D-pad ↓"), "dl" => Txt.L("Kreuz ←", "D-pad ←"), "dr" => Txt.L("Kreuz →", "D-pad →"),
        "lsu" => "L-Stick ↑", "lsd" => "L-Stick ↓", "lsl" => "L-Stick ←", "lsr" => "L-Stick →",
        "rsu" => "R-Stick ↑", "rsd" => "R-Stick ↓", "rsl" => "R-Stick ←", "rsr" => "R-Stick →",
        _ => "–",
    };

    /// <summary>A stick direction ("lsu" …) as (axis, positive): SDL numbers the sticks 0/1 (left) and 2/3 (right), down and right are +.</summary>
    static (int Axis, bool Positive)? StickOf(string pad) => pad.Length == 3 && pad[0] is 'l' or 'r' && pad[1] == 's'
        ? ((pad[0] == 'l' ? 0 : 2) + (pad[2] is 'u' or 'd' ? 1 : 0), pad[2] is 'd' or 'r')
        : null;

    /// <summary>A controller d-pad direction ("du" …) as SDL hat bit: up 1, right 2, down 4, left 8.</summary>
    static int? HatOf(string pad) => pad switch { "du" => 1, "dr" => 2, "dd" => 4, "dl" => 8, _ => null };

    /// <summary>The controller inputs that move on the DS/GBA for one direction: "Move", and the d-pad row when it is a d-pad or stick.</summary>
    IEnumerable<string> DsDirection(string dir) =>
        new[] { Pad.GetValueOrDefault("Move" + dir, ""), Pad.GetValueOrDefault("Pad" + dir, "") }
            .Where((p, i) => p.Length > 0 && (i == 0 || HatOf(p) != null || StickOf(p) != null)).Distinct();

    [StructLayout(LayoutKind.Sequential)]
    struct XInputState { public uint Packet; public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY; }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetState(uint index, out XInputState state);

    /// <summary>
    /// The emulators that read the controller through SDL's joystick layer (melonDS, mGBA) see an Xbox pad differently on
    /// Linux: there the left trigger is axis 2 and the right stick 3/4 (Windows: stick 2/3, triggers 4/5), and the Guide
    /// button sits between Menu and the stick buttons (L3 = 9, R3 = 10 instead of 8, 9). Azahar asks by name and is the same.
    /// </summary>
    static int JoyAxis(int axis) => Os.Windows ? axis : axis switch { 2 => 3, 3 => 4, 4 => 2, _ => axis };

    static int JoyButton(int button) => Os.Windows || button < 8 ? button : button + 1;

    /// <summary>A hub button ("b8") as the number the SDL joystick emulators use, "-1" if it is none.</summary>
    static string JoyButton(string pad) => pad.StartsWith('b') && int.TryParse(pad.AsSpan(1), out int b) ? JoyButton(b).ToString() : "-1";

    /// <summary>Number of the first connected Xbox-style controller, or null.</summary>
    public static int? ConnectedPad()
    {
        if (!Os.Windows) return LinuxPad.Any() ? 0 : null;
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

    /// <summary>The input held right now on any controller ("b0", "lt", "du", "lsl" …), or null.</summary>
    public static string? PressedPad()
    {
        if (!Os.Windows) return LinuxPad.Pressed();
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
                foreach (var (bit, id) in new (ushort, string)[] { (0x0001, "du"), (0x0002, "dd"), (0x0004, "dl"), (0x0008, "dr") })
                    if ((s.Buttons & bit) != 0) return id;
                // a stick pushed well over half way (XInput: up is +)
                const int Far = 20000;
                if (s.LY > Far) return "lsu";
                if (s.LY < -Far) return "lsd";
                if (s.LX < -Far) return "lsl";
                if (s.LX > Far) return "lsr";
                if (s.RY > Far) return "rsu";
                if (s.RY < -Far) return "rsd";
                if (s.RX < -Far) return "rsl";
                if (s.RX > Far) return "rsr";
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
        // directions: the "Move" key; on the controller one button-or-hat plus one stick axis per direction
        foreach (var dir in Directions)
        {
            HubSetup.SetToml(lines, "Instance0.Keyboard", dir, Keys.GetValueOrDefault("Move" + dir, -1).ToString());
            HubSetup.SetToml(lines, "Instance0.Joystick", dir, MelonDirection(DsDirection(dir)).ToString());
        }
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

    /// <summary>
    /// melonDS joystick value of a direction: low 16 bits a button (0x100 | hat&lt;&lt;4 | hat direction for the d-pad, 0xFFFF
    /// none), plus 0x10000 | axis&lt;&lt;24 | (0 = +, 1 = −)&lt;&lt;20 for a stick – melonDS checks both.
    /// </summary>
    static int MelonDirection(IEnumerable<string> pads)
    {
        int? button = null, axis = null;
        foreach (var pad in pads)
        {
            if (StickOf(pad) is { } stick) axis ??= 0x10000 | (JoyAxis(stick.Axis) << 24) | ((stick.Positive ? 0 : 1) << 20);
            else if (HatOf(pad) is { } hat) button ??= 0x100 | hat;
            else if (MelonPad(pad) is >= 0 and < 0xFFFF and var b) button ??= b;
        }
        if (button == null && axis == null) return -1;
        return (button ?? 0xFFFF) | (axis ?? 0);
    }

    /// <summary>melonDS joystick value: a button's number, triggers as axis 4/5 (the codes melonDS writes itself).</summary>
    static int MelonPad(string pad) => pad switch
    {
        "lt" => (JoyAxis(4) << 24) | 0x0021FFFF,
        "rt" => (JoyAxis(5) << 24) | 0x0021FFFF,
        _ when pad.StartsWith('b') && int.TryParse(pad.AsSpan(1), out int button) => JoyButton(button),
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
                HubSetup.SetIni(lines, "gba.input.SDLB", axisKey + "Axis", "+" + JoyAxis(pad == "lt" ? 4 : 5));
                HubSetup.SetIni(lines, "gba.input.SDLB", axisKey + "Value", "-20480");
            }
            else
            {
                HubSetup.SetIni(lines, "gba.input.SDLB", key, JoyButton(pad));
                HubSetup.RemoveIni(lines, "gba.input.SDLB", axisKey + "Axis");
                HubSetup.RemoveIni(lines, "gba.input.SDLB", axisKey + "Value");
            }
        }
        // directions (GBA keys: 4 right, 5 left, 6 up, 7 down): the "Move" key; on the controller a button, the d-pad (hat 0)
        // and/or a stick – written out completely, so they are there even when mGBA takes this section as the whole setup
        var gbaKey = new Dictionary<string, string> { ["Up"] = "6", ["Down"] = "7", ["Left"] = "5", ["Right"] = "4" };
        var hatName = new Dictionary<int, string> { [1] = "Up", [2] = "Right", [4] = "Down", [8] = "Left" };
        foreach (var dir in Directions)
        {
            HubSetup.SetIni(lines, "gba.input.QT_K", "key" + dir, Keys.GetValueOrDefault("Move" + dir, -1).ToString());
            HubSetup.SetIni(lines, "gba.input.SDLB", "key" + dir, "-1");
            HubSetup.RemoveIni(lines, "gba.input.SDLB", "hat0" + dir);
            HubSetup.RemoveIni(lines, "gba.input.SDLB", $"axis{dir}Axis");
            HubSetup.RemoveIni(lines, "gba.input.SDLB", $"axis{dir}Value");
        }
        foreach (var dir in Directions)
        {
            bool hasStick = false;
            foreach (var pad in DsDirection(dir))
            {
                if (StickOf(pad) is { } stick)
                {
                    // one stick per direction, as in melonDS: "Move" comes first and wins
                    if (hasStick) continue;
                    hasStick = true;
                    HubSetup.SetIni(lines, "gba.input.SDLB", $"axis{dir}Axis", (stick.Positive ? "+" : "-") + JoyAxis(stick.Axis));
                    HubSetup.SetIni(lines, "gba.input.SDLB", $"axis{dir}Value", stick.Positive ? "12288" : "-12288");
                }
                else if (HatOf(pad) is { } hat) HubSetup.SetIni(lines, "gba.input.SDLB", "hat0" + hatName[hat], gbaKey[dir]);
                else if (pad.StartsWith('b')) HubSetup.SetIni(lines, "gba.input.SDLB", "key" + dir, JoyButton(pad));
            }
        }
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        HubSetup.SetIni(lines, "ports.qt", "fastForwardHeldRatio", FastForward.ToString(invariant));
        HubSetup.SetIni(lines, "ports.qt", "fastForwardRatio", Toggle.ToString(invariant));
    }

    static readonly (string Action, string Button)[] AzaharButtons =
        [("A", "a"), ("B", "b"), ("X", "x"), ("Y", "y"), ("L", "l"), ("R", "r"), ("ZL", "zl"), ("ZR", "zr"), ("Start", "start"), ("Select", "select"),
         ("PadUp", "up"), ("PadDown", "down"), ("PadLeft", "left"), ("PadRight", "right")];

    static string AzaharKey(int code) => code > 0 ? $"code:{code},engine:keyboard" : AzaharNone;

    /// <summary>
    /// An emptied 3DS input: Azahar puts its own default in for an empty value, so "no input" is a key that doesn't exist.
    /// (Inside the circle pad an empty value is fine – there it means "not set".)
    /// </summary>
    const string AzaharNone = "code:0,engine:keyboard";

    /// <summary>Azahar's way to nest parameters (Citra's ParamPackage): ':' → $0, ',' → $1, '$' → $2.</summary>
    static string Nested(string param) => param.Replace("$", "$2").Replace(",", "$1").Replace(":", "$0");

    /// <summary>The circle pad from four inputs (keys or controller inputs).</summary>
    static string CirclePadFromButtons(string up, string down, string left, string right, string modifier = "") =>
        $"down:{Nested(down)},engine:analog_from_button,left:{Nested(left)}," +
        (modifier.Length > 0 ? $"modifier:{Nested(modifier)},modifier_scale:0.500000," : "") +
        $"right:{Nested(right)},up:{Nested(up)}";

    /// <summary>
    /// Azahar qt-config.ini. Azahar binds each 3DS input to ONE input, so there are two profiles: 1 = keyboard,
    /// 2 = controller (any connected one – "maptype:all" needs no device ID). <see cref="Azahar3ds.Prepare"/> picks the
    /// controller profile when a controller is plugged in. "Move" is the circle pad, "D-pad" the 3DS d-pad. "Tempo an/aus"
    /// is Azahar's turbo switch on both (Azahar has no hold-to-speed-up). <paramref name="keyboard"/>: also write the
    /// keyboard profile (else the player's stays).
    /// </summary>
    internal void ApplyToAzahar(List<string> lines, bool keyboard)
    {
        if (keyboard)
        {
            foreach (var (action, button) in AzaharButtons)
                Azahar3ds.Set(lines, "Controls", $@"profiles\1\button_{button}", $"\"{AzaharKey(Keys.GetValueOrDefault(action, -1))}\"");
            // the circle pad from the "Move" keys; D held = half way, as in Azahar's own setup
            var move = Directions.Select(d => Keys.GetValueOrDefault("Move" + d, -1) is > 0 and var k ? AzaharKey(k) : "").ToArray();
            Azahar3ds.Set(lines, "Controls", @"profiles\1\circle_pad", $"\"{CirclePadFromButtons(move[0], move[1], move[2], move[3], AzaharKey('D'))}\"");
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Toggle%20Turbo%20Mode\KeySeq", KeyName(Keys.GetValueOrDefault("Toggle", -1)));
        }
        HubSetup.SetIni(lines, "Controls", @"profiles\1\name", Txt.L("Tastatur", "Keyboard"));
        HubSetup.SetIni(lines, "Controls", @"profiles\2\name", ControllerProfile);
        foreach (var (action, button) in AzaharButtons)
            Azahar3ds.Set(lines, "Controls", $@"profiles\2\button_{button}", $"\"{AzaharPad(Pad.GetValueOrDefault(action, "")) ?? AzaharNone}\"");
        Azahar3ds.Set(lines, "Controls", @"profiles\2\circle_pad", $"\"{AzaharCirclePad()}\"");
        HubSetup.SetIni(lines, "Controls", @"profiles\size", "2");
        if (AzaharPad(Pad.GetValueOrDefault("Toggle", "")) is { } turbo)
            HubSetup.SetIni(lines, "Shortcuts", @"Main%20Window\Toggle%20Turbo%20Mode\controller_keyseq", $"\"{turbo}\"");
        Azahar3ds.Set(lines, "Renderer", "turbo_limit", Math.Round(Toggle * 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal const string ControllerProfile = "Controller"; // plain ASCII: Azahar writes it back unchanged
    const string AllControllers = "api:controller,engine:sdl,guid:0,maptype:all,port:0";

    /// <summary>The circle pad on the controller: a whole stick when "Move" is one stick the normal way round, else built from the four inputs.</summary>
    string AzaharCirclePad()
    {
        var move = Directions.Select(d => Pad.GetValueOrDefault("Move" + d, "")).ToArray();
        if (move.SequenceEqual(["lsu", "lsd", "lsl", "lsr"])) return $"{AllControllers},axis_x:0,axis_y:1,deadzone:0.100000";
        if (move.SequenceEqual(["rsu", "rsd", "rsl", "rsr"])) return $"{AllControllers},axis_x:2,axis_y:3,deadzone:0.100000";
        var p = move.Select(m => AzaharPad(m) ?? "").ToArray();
        return CirclePadFromButtons(p[0], p[1], p[2], p[3]);
    }

    /// <summary>A hub controller input ("b0" = SDL joystick button of an Xbox pad, "lt", "du", "lsl") as Azahar controller-API parameters.</summary>
    static string? AzaharPad(string pad) => pad switch
    {
        "lt" => $"{AllControllers},axis:4,direction:+,threshold:0.500000",
        "rt" => $"{AllControllers},axis:5,direction:+,threshold:0.500000",
        "du" => $"{AllControllers},button:11",
        "dd" => $"{AllControllers},button:12",
        "dl" => $"{AllControllers},button:13",
        "dr" => $"{AllControllers},button:14",
        // Azahar: "+" = pressed above the threshold, "-" = pressed BELOW it – so up/left need a negative one
        // (with +0.5 a resting stick at 0 counts as held: the d-pad was stuck up-left)
        _ when StickOf(pad) is { } stick => $"{AllControllers},axis:{stick.Axis},direction:{(stick.Positive ? "+" : "-")},threshold:{(stick.Positive ? "" : "-")}0.500000",
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
        if (!Os.Windows) return false;
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
            HubSetup.SetIni(lines, "shortcutButton", name, JoyButton(pad));
            if (pad is "lt" or "rt") HubSetup.SetIni(lines, "shortcutAxis", name, "+" + JoyAxis(pad == "lt" ? 4 : 5));
            else HubSetup.RemoveIni(lines, "shortcutAxis", name);
        }
    }
}
