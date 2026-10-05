using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MonHub;

/// <summary>
/// What Windows and Linux do differently, in one place: where a program's file is, how it is started, the bin deleted
/// things go to, showing a file in the file manager, and reading a controller.
/// </summary>
public static class Os
{
    public static bool Windows => OperatingSystem.IsWindows();

    /// <summary>"Recycle Bin" / "Papierkorb" on Windows, the "trash" on Linux – for texts.</summary>
    public static string Bin => Windows ? Txt.L("Papierkorb", "Recycle Bin") : Txt.L("Papierkorb", "trash");

    // ---------------- programs ----------------

    /// <summary>
    /// The start file of an emulator in its folder. Windows: "name*.exe". Linux: an unpacked AppImage ("app/AppRun"),
    /// an AppImage, or a plain program called like the emulator.
    /// </summary>
    public static string? FindProgram(string folder, string name)
    {
        if (!Directory.Exists(folder)) return null;
        try
        {
            if (Windows)
                return Directory.GetFiles(folder, name + "*.exe").OrderByDescending(f => f.Contains("x64")).FirstOrDefault();
            var unpacked = Path.Combine(folder, "app", "AppRun");
            if (File.Exists(unpacked)) return unpacked;
            return Directory.EnumerateFiles(folder).FirstOrDefault(f => f.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                ?? Directory.EnumerateFiles(folder).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a program or opens a file. An emulator from MonHub's own folder keeps its settings inside that folder on
    /// Linux too (there they would otherwise go to the home folder and mix with an emulator the player installed).
    /// </summary>
    public static void Start(string file, string? args, string workingDir)
    {
        if (Windows)
        {
            var psi = new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = workingDir };
            if (args != null) psi.Arguments = args;
            Process.Start(psi)?.Dispose();
            return;
        }
        if (!IsProgram(file))
        {
            // a ROM or document without an emulator of ours: whatever the desktop opens it with
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { file }, UseShellExecute = false })?.Dispose();
            return;
        }
        var start = new ProcessStartInfo(file) { UseShellExecute = false, WorkingDirectory = workingDir };
        if (args != null) start.Arguments = args;
        // an AppImage that install.sh could not unpack: let it unpack itself for this run (works without FUSE)
        if (file.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)) start.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
        // libraries a fresh system may lack (libOpenGL on Ubuntu) come with MonHub – asked last, so the system's own win
        var reserve = Path.Combine(HubPaths.Emulators, "lib");
        if (Directory.Exists(reserve))
            start.Environment["LD_LIBRARY_PATH"] = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") is { Length: > 0 } own ? own + ":" + reserve : reserve;
        if (EmulatorHome(file) is { } home)
        {
            start.WorkingDirectory = home; // Azahar: a "user" folder next to where it is started = portable
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, "config");
            start.Environment["XDG_DATA_HOME"] = Path.Combine(home, "data");
            Directory.CreateDirectory(Path.Combine(home, "config"));
            Directory.CreateDirectory(Path.Combine(home, "data"));
            // what the emulator says while starting goes into a small file; "exec" keeps it the same program
            // (MonHub tells running emulators by their command line)
            var log = Path.Combine(home, "start.log");
            start.Environment["MONHUB_START_LOG"] = log;
            start.FileName = "/bin/sh";
            start.Arguments = $"-c \"exec \\\"$0\\\" \\\"$@\\\" 2>\\\"$MONHUB_START_LOG\\\"\" \"{file}\" {args}";
            var started = DateTime.UtcNow;
            var process = Process.Start(start);
            if (process == null) return;
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                int code = process.ExitCode;
                process.Dispose();
                // gone within seconds with an error: it never came up – say why instead of nothing happening
                if (code == 0 || DateTime.UtcNow - started > TimeSpan.FromSeconds(8)) return;
                string why;
                try { why = string.Join("\n", File.ReadLines(log).Where(l => l.Trim().Length > 0).TakeLast(4)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { why = ""; }
                Dispatcher.UIThread.Post(() => MessageBox.Show(
                    Txt.L($"{Path.GetFileName(home)} ist gleich wieder zugegangen.", $"{Path.GetFileName(home)} closed again right away.")
                    + (why.Length > 0 ? "\n\n" + (why.Length > 600 ? why[..600] + " …" : why) : ""),
                    "MonHub", MessageBoxButton.OK, MessageBoxImage.Warning));
            };
            return;
        }
        Process.Start(start)?.Dispose();
    }

    /// <summary>The emulator folder (System/Emulatoren/&lt;name&gt;) a program belongs to, or null for other programs.</summary>
    static string? EmulatorHome(string file)
    {
        var root = HubPaths.Emulators.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!file.StartsWith(root, StringComparison.Ordinal)) return null;
        var name = file[root.Length..].Split(Path.DirectorySeparatorChar)[0];
        return name.Length > 0 ? Path.Combine(HubPaths.Emulators, name) : null;
    }

    /// <summary>Linux: whether the file may be executed (a program, an AppImage, a script).</summary>
    static bool IsProgram(string file)
    {
        try
        {
            return (File.GetUnixFileMode(file) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Shows a file selected in the file manager (Explorer; on Linux the desktop's file manager, else its folder).</summary>
    public static void ShowInFolder(string file)
    {
        if (Windows)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true })?.Dispose();
            return;
        }
        try
        {
            // the common way of GNOME Files, Dolphin, Nemo, Thunar …
            var ask = Process.Start(new ProcessStartInfo("dbus-send")
            {
                ArgumentList =
                {
                    "--session", "--print-reply", "--dest=org.freedesktop.FileManager1", "--type=method_call", "/org/freedesktop/FileManager1",
                    "org.freedesktop.FileManager1.ShowItems", "array:string:" + new Uri(file).AbsoluteUri, "string:",
                },
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (ask != null && ask.WaitForExit(3000) && ask.ExitCode == 0) return;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no dbus-send: open the folder
        }
        Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { Path.GetDirectoryName(file) ?? file }, UseShellExecute = false })?.Dispose();
    }

    // ---------------- the bin ----------------

    /// <summary>Linux: files and folders into the trash (restorable from the file manager). False if one could not be moved.</summary>
    public static bool Trash(string[] paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        if (existing.Length == 0) return true;
        try
        {
            var gio = new ProcessStartInfo("gio") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            gio.ArgumentList.Add("trash");
            gio.ArgumentList.Add("--");
            foreach (var path in existing) gio.ArgumentList.Add(path);
            using var run = Process.Start(gio);
            if (run != null && run.WaitForExit(30000) && run.ExitCode == 0) return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // no gio on this system: the trash folder itself
        }
        bool all = true;
        foreach (var path in existing.Where(p => File.Exists(p) || Directory.Exists(p)))
            all &= TrashByHand(path);
        return all;
    }

    /// <summary>The freedesktop trash in the home folder: the file under "files", a note where it came from under "info".</summary>
    static bool TrashByHand(string path)
    {
        try
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            string files = Path.Combine(data, "Trash", "files"), info = Path.Combine(data, "Trash", "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            var target = name;
            for (int i = 2; File.Exists(Path.Combine(files, target)) || Directory.Exists(Path.Combine(files, target)); i++)
                target = $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}";
            File.WriteAllText(Path.Combine(info, target + ".trashinfo"),
                $"[Trash Info]\nPath={Uri.EscapeDataString(Path.GetFullPath(path)).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
            if (Directory.Exists(path)) Directory.Move(path, Path.Combine(files, target));
            else File.Move(path, Path.Combine(files, target));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// Linux: the controller through the kernel's joystick devices (/dev/input/js0 …). One small reader per device keeps
/// what is held; an Xbox-style pad is numbered like this: buttons A B X Y LB RB View Menu Guide L3 R3, axes left stick
/// 0/1, LT 2, right stick 3/4, RT 5, d-pad 6/7.
/// </summary>
static class LinuxPad
{
    sealed class Device
    {
        public readonly short[] Axes = new short[16];
        public readonly bool[] Buttons = new bool[32];
        public volatile bool Alive = true;
    }

    static readonly Dictionary<string, Device> Devices = new();

    static IEnumerable<string> Nodes()
    {
        try
        {
            return Directory.Exists("/dev/input") ? Directory.GetFiles("/dev/input", "js*").Where(IsGamepad).ToArray() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Not everything with a "js" device is a controller (a virtual machine's mouse, a drawing tablet): a controller
    /// has gamepad or joystick buttons (key codes 0x120–0x13F), which the kernel lists as a bit field.
    /// </summary>
    static bool IsGamepad(string node)
    {
        try
        {
            var words = File.ReadAllText($"/sys/class/input/{Path.GetFileName(node)}/device/capabilities/key").Trim().Split(' ');
            // the last word holds keys 0–63, the one before 64–127 …; 0x120–0x13F are bits 32–63 of the fifth word
            if (words.Length < 5) return false;
            return (Convert.ToUInt64(words[^5], 16) >> 32) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return false;
        }
    }

    public static bool Any() => Nodes().Any();

    /// <summary>The input held right now ("b0", "lt", "du", "lsl" …), or null.</summary>
    public static string? Pressed()
    {
        lock (Devices)
        {
            foreach (var gone in Devices.Where(d => !d.Value.Alive).Select(d => d.Key).ToList()) Devices.Remove(gone);
            foreach (var node in Nodes())
                if (!Devices.ContainsKey(node)) Devices[node] = Open(node);
            foreach (var device in Devices.Values)
                if (Held(device) is { } id) return id;
        }
        return null;
    }

    static Device Open(string node)
    {
        var device = new Device();
        var thread = new Thread(() =>
        {
            try
            {
                using var stream = new FileStream(node, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8, FileOptions.None);
                var e = new byte[8]; // js_event: time (4), value (2), type (1), number (1)
                while (stream.Read(e, 0, 8) == 8)
                {
                    short value = BitConverter.ToInt16(e, 4);
                    int type = e[6] & 0x7F, number = e[7]; // 0x80 = the state when the device was opened
                    if (type == 1 && number < device.Buttons.Length) device.Buttons[number] = value != 0;
                    else if (type == 2 && number < device.Axes.Length) device.Axes[number] = value;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // unplugged, or not ours to read
            }
            device.Alive = false;
        }) { IsBackground = true, Name = "MonHub controller " + Path.GetFileName(node) };
        thread.Start();
        return device;
    }

    static string? Held(Device d)
    {
        (int Button, string Id)[] map = [(0, "b0"), (1, "b1"), (2, "b2"), (3, "b3"), (4, "b4"), (5, "b5"), (6, "b6"), (7, "b7"), (9, "b8"), (10, "b9")];
        foreach (var (button, id) in map)
            if (d.Buttons[button]) return id;
        const int Far = 20000;
        if (d.Axes[2] > 0) return "lt";
        if (d.Axes[5] > 0) return "rt";
        if (d.Axes[7] < -Far) return "du";
        if (d.Axes[7] > Far) return "dd";
        if (d.Axes[6] < -Far) return "dl";
        if (d.Axes[6] > Far) return "dr";
        // sticks: up and left are negative here
        if (d.Axes[1] < -Far) return "lsu";
        if (d.Axes[1] > Far) return "lsd";
        if (d.Axes[0] < -Far) return "lsl";
        if (d.Axes[0] > Far) return "lsr";
        if (d.Axes[4] < -Far) return "rsu";
        if (d.Axes[4] > Far) return "rsd";
        if (d.Axes[3] < -Far) return "rsl";
        if (d.Axes[3] > Far) return "rsr";
        return null;
    }
}
