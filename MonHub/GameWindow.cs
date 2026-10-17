using System.Runtime.InteropServices;

namespace MonHub;

/// <summary>
/// The emulator's own window, hung into MonHub: on Windows as a child window (SetParent), on Linux as an X11 child
/// (XReparentWindow – Avalonia and the emulators both run on X11 there, on Wayland desktops through XWayland).
/// Whatever can't be found or taken stays the emulator's own window, as before.
/// </summary>
public abstract class GameWindow
{
    public static GameWindow? Create() => OperatingSystem.IsWindows() ? new Win32GameWindow() : OperatingSystem.IsLinux() ? X11GameWindow.TryCreate() : null;

    /// <summary>The emulator's main window: visible, top-level, not a dialog – from one of these processes; 0 = not (yet) there.</summary>
    public abstract IntPtr Find(IReadOnlyCollection<int> pids);

    /// <summary>Takes the window into the box (MonHub's place for it) at the given size in pixels.</summary>
    public abstract void Take(IntPtr game, IntPtr box);

    /// <summary>Puts the window at 0,0 of the box with this size.</summary>
    public abstract void Place(IntPtr game, int x, int y, int width, int height);

    /// <summary>The box's size in pixels (what Avalonia gave it).</summary>
    public abstract (int Width, int Height) Size(IntPtr box);

    /// <summary>Keys go to the game.</summary>
    public abstract void Focus(IntPtr game);

    /// <summary>Gives the window back to the desktop as the emulator's own window, where the box was.</summary>
    public abstract void Release(IntPtr game, IntPtr box);

    /// <summary>Asks the emulator to close, like its own close button (it saves on the way).</summary>
    public abstract void Close(IntPtr game);

    /// <summary>The place moved on the screen: the window is told so (it keeps its spot inside the place).</summary>
    public virtual void Moved(IntPtr game) { }

    /// <summary>The window's title (null where it isn't read) – emulators name the running game there.</summary>
    public virtual string? Title(IntPtr game) => null;

    /// <summary>Whether the emulator answers right now (false while it is busy, e.g. loading the game).</summary>
    public virtual bool Responsive(IntPtr game) => true;

    /// <summary>The game is up: no more asking whether it answers (DeSmuME stops drawing when asked all the time).</summary>
    public virtual void Settled(IntPtr game) { }

    /// <summary>
    /// The place made invisible (true) or shown again – it keeps its size, so the emulator has its full room while it
    /// starts, and MonHub's loading screen below it shows meanwhile.
    /// </summary>
    public virtual void Mask(IntPtr box, bool hidden) { }

    /// <summary>Whether the window still exists (an emulator may build a new one, e.g. after changing its renderer).</summary>
    public abstract bool Exists(IntPtr game);

    /// <summary>The mouse on the screen, in pixels.</summary>
    public abstract (int X, int Y)? Cursor();

    /// <summary>The emulator's own child windows: place inside the game window and size, in pixels.</summary>
    protected abstract List<(int X, int Y, int Width, int Height)> Children(IntPtr game, out int width, out int height);

    /// <summary>
    /// How much of the game window isn't picture (menu bar, toolbar, status bar), in pixels: around the picture's own
    /// window if there is one, otherwise the bars along its top and bottom edge. Null while it can't be told.
    /// </summary>
    public (int Left, int Top, int Right, int Bottom)? Chrome(IntPtr game)
    {
        var children = Children(game, out int w, out int h);
        if (w <= 0 || h <= 0) return null;
        var picture = children.Where(c => c.Width * c.Height >= w * h * 2 / 5).OrderByDescending(c => c.Width * c.Height).FirstOrDefault();
        if (picture.Width > 0)
            return (Math.Max(0, picture.X), Math.Max(0, picture.Y), Math.Max(0, w - picture.X - picture.Width), Math.Max(0, h - picture.Y - picture.Height));
        // bars: as wide as the window, low, at the top or the bottom edge
        var bars = children.Where(c => c.Width >= w * 4 / 5 && c.Height < h / 4).ToList();
        int top = bars.Where(c => c.Y <= 2).Select(c => c.Y + c.Height).DefaultIfEmpty(0).Max();
        int bottom = bars.Where(c => c.Y + c.Height >= h - 2 && c.Y > h / 2).Select(c => h - c.Y).DefaultIfEmpty(0).Max();
        return (0, top, 0, bottom);
    }

    /// <summary>Every process started from this one (the start file may hand over to the real program).</summary>
    public static List<int> Family(int pid)
    {
        var all = new List<int> { pid };
        if (!OperatingSystem.IsLinux()) return all;
        try
        {
            var parents = new Dictionary<int, int>();
            foreach (var dir in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(dir), out int p)) continue;
                try
                {
                    // "pid (name) state ppid …" – the name may hold spaces and brackets, so read from the last ')'
                    var stat = File.ReadAllText(Path.Combine(dir, "stat"));
                    var rest = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    parents[p] = int.Parse(rest[1]);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                {
                }
            }
            for (int i = 0; i < all.Count; i++)
                foreach (var (child, parent) in parents)
                    if (parent == all[i] && !all.Contains(child)) all.Add(child);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return all;
    }
}

sealed class Win32GameWindow : GameWindow
{
    const long WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000,
        WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_MAXIMIZE = 0x01000000, WS_MINIMIZE = 0x20000000;
    const long WS_EX_APPWINDOW = 0x00040000, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_DLGMODALFRAME = 0x1;
    const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    // SWP_NOCOPYBITS: on a resize Windows would copy the old picture into the new size – the emulator's last frame showed
    // up a second time at the edge until it drew again
    const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_SHOWWINDOW = 0x40, SWP_NOCOPYBITS = 0x100;

    // the window's own look, for giving it back
    readonly Dictionary<IntPtr, (long Style, long ExStyle)> _own = new();

    // Moving, sizing and focusing another program's window waits until that program answers – an emulator busy loading
    // its game (Azahar: seconds) would freeze MonHub. So all of that runs here, on a thread of its own: a busy emulator
    // only makes this thread wait.
    readonly System.Collections.Concurrent.BlockingCollection<Action> _work = new();
    readonly object _gate = new();
    (IntPtr Game, int X, int Y, int W, int H)? _placeWanted;
    bool _placeQueued;
    volatile IntPtr _watched;
    volatile bool _answers = true;
    long _busySince; // ticks since an action started on the window thread, 0 = idle

    public Win32GameWindow()
    {
        new Thread(Work) { IsBackground = true, Name = "MonHub game window" }.Start();
    }

    /// <summary>
    /// On the window thread – but only for Qt programs (the slow starters). A plain Windows program (DeSmuME) is handled
    /// right here: taken in from another thread, DeSmuME never draws its picture.
    /// </summary>
    void Do(IntPtr game, Action action)
    {
        if (IsQt(game)) _work.Add(action);
        else action();
    }

    static bool IsQt(IntPtr game)
    {
        var cls = new System.Text.StringBuilder(64);
        GetClassName(game, cls, 64);
        return cls.ToString().StartsWith("Qt", StringComparison.Ordinal);
    }

    void Work()
    {
        while (true)
        {
            if (_work.TryTake(out var action, 250))
            {
                Interlocked.Exchange(ref _busySince, DateTime.UtcNow.Ticks);
                try { action(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                Interlocked.Exchange(ref _busySince, 0);
            }
            // does the emulator answer? (asked here, so a busy one only makes this thread wait)
            var game = _watched;
            if (game != IntPtr.Zero)
                _answers = SendMessageTimeout(game, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero, 0x2 /* SMTO_ABORTIFHUNG */, 300, out _) != IntPtr.Zero;
        }
    }

    public override void Settled(IntPtr game)
    {
        if (_watched == game) _watched = IntPtr.Zero;
    }

    public override bool Responsive(IntPtr game)
    {
        long since = Interlocked.Read(ref _busySince);
        if (since != 0 && DateTime.UtcNow.Ticks - since > TimeSpan.FromMilliseconds(200).Ticks) return false; // stuck in a call to it
        return _watched == IntPtr.Zero ? _answers : _watched == game && _answers;
    }

    /// <summary>An empty window region makes the place (and the game window inside it) invisible; null shows it again.</summary>
    public override void Mask(IntPtr box, bool hidden)
    {
        if (box == IntPtr.Zero) return;
        // the place and the window Avalonia holds it in (grey of its own) – not MonHub's main window
        var root = GetAncestor(box, 2 /* GA_ROOT */);
        for (var h = box; h != IntPtr.Zero && h != root; h = GetParent(h))
            SetWindowRgn(h, hidden ? CreateRectRgn(0, 0, 0, 0) : IntPtr.Zero, true); // the system owns the region from here on
    }

    /// <summary>
    /// Starts a program whose first window comes up minimized and inactive (SW_SHOWMINNOACTIVE): only a taskbar button
    /// for a moment, nothing on the screen, the focus stays where it is. Null if it couldn't be started that way.
    /// </summary>
    public static System.Diagnostics.Process? StartMinimized(string file, string? args, string workingDir)
    {
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), dwFlags = 0x1 /* STARTF_USESHOWWINDOW */, wShowWindow = 7 /* SW_SHOWMINNOACTIVE */ };
        var line = new System.Text.StringBuilder($"\"{file}\"" + (args != null ? " " + args : ""));
        if (!CreateProcess(file, line, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, workingDir, ref si, out var pi)) return null;
        try
        {
            return System.Diagnostics.Process.GetProcessById(pi.dwProcessId);
        }
        catch (ArgumentException)
        {
            return null; // gone already
        }
        finally
        {
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
    }

    public override IntPtr Find(IReadOnlyCollection<int> pids)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out uint p);
            // GW_OWNER: dialogs (Azahar's "update available") have an owner, the main window doesn't
            // (a minimized one is only a small bar far off the screen – it counts too)
            if (pids.Contains((int)p) && IsWindowVisible(h) && GetWindow(h, 4) == IntPtr.Zero && (IsIconic(h) || GetWindowRect(h, out var r) && r.R - r.L > 100 && r.B - r.T > 80))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // the window's frame (title bar, borders) around its inside, in pixels – kept, but pushed out of the place
    readonly Dictionary<IntPtr, (int L, int T, int R, int B)> _frame = new();

    /// <summary>
    /// A Qt program's window keeps its frame and only becomes a child window; the frame is then pushed out over the place's edges,
    /// where it is cut off. (Taking the frame away instead confuses Qt 6 programs – melonDS, Azahar: they keep reckoning
    /// with the old frame and leave a strip of their window empty.)
    /// </summary>
    public override void Take(IntPtr game, IntPtr box)
    {
        // only the slow Qt starters are asked whether they answer; DeSmuME is ready at once
        bool qt = IsQt(game);
        _watched = qt ? game : IntPtr.Zero;
        _answers = !qt; // not known yet for the others
        Do(game, () => TakeNow(game, box));
    }

    void TakeNow(IntPtr game, IntPtr box)
    {
        long style = GetWindowLongPtr(game, GWL_STYLE).ToInt64(), ex = GetWindowLongPtr(game, GWL_EXSTYLE).ToInt64();
        _own[game] = (style & ~WS_MINIMIZE, ex); // given back as a normal window, not a minimized one
        // Qt programs keep their frame (see above); a plain Windows program (DeSmuME) loses it – pushed out over the
        // edge, DeSmuME would draw its picture shifted
        bool qt = IsQt(game);
        long frame = qt ? 0 : WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
        SetWindowLongPtr(game, GWL_STYLE, new IntPtr(style & ~(WS_POPUP | WS_MAXIMIZE | WS_MINIMIZE | frame) | WS_CHILD));
        SetWindowLongPtr(game, GWL_EXSTYLE, new IntPtr(ex & ~(WS_EX_APPWINDOW | (qt ? 0 : WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_DLGMODALFRAME))));
        SetParent(game, box);
        // it was started minimized (see StartMinimized): back to its normal state, now inside MonHub
        if (IsIconic(game)) ShowWindow(game, 4 /* SW_SHOWNOACTIVATE */);
        var (w, h) = Size(box);
        SetWindowPos(game, IntPtr.Zero, 0, 0, w, h, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW | SWP_NOCOPYBITS);
        _frame[game] = Frame(game);
        PlaceNow(game, 0, 0, w, h);
    }

    /// <summary>How far the window reaches beyond its inside on each side.</summary>
    static (int L, int T, int R, int B) Frame(IntPtr game)
    {
        if (!GetWindowRect(game, out var outer) || !GetClientRect(game, out var inner)) return default;
        var origin = new POINT();
        ClientToScreen(game, ref origin);
        return (origin.X - outer.L, origin.Y - outer.T, outer.R - (origin.X + inner.R), outer.B - (origin.Y + inner.B));
    }

    /// <summary>The window's inside goes exactly onto this rectangle of the place (its frame beyond it).</summary>
    public override void Place(IntPtr game, int x, int y, int width, int height)
    {
        // only the newest wish counts: while the emulator is busy, a window resize asks many times
        lock (_gate)
        {
            _placeWanted = (game, x, y, width, height);
            if (_placeQueued) return;
            _placeQueued = true;
        }
        Do(game, () =>
        {
            (IntPtr Game, int X, int Y, int W, int H)? wanted;
            lock (_gate)
            {
                wanted = _placeWanted;
                _placeQueued = false;
            }
            if (wanted is { } p) PlaceNow(p.Game, p.X, p.Y, p.W, p.H);
        });
    }

    void PlaceNow(IntPtr game, int x, int y, int width, int height)
    {
        var f = _frame.TryGetValue(game, out var known) ? known : default;
        int wx = x - f.L, wy = y - f.T, ww = width + f.L + f.R, wh = height + f.T + f.B;
        if (GetWindowRect(game, out var r) && GetParent(game) is var parent && parent != IntPtr.Zero)
        {
            // already there: no move (a move makes some emulators redraw)
            var p = new POINT { X = r.L, Y = r.T };
            ScreenToClient(parent, ref p);
            if (p.X == wx && p.Y == wy && r.R - r.L == ww && r.B - r.T == wh) return;
        }
        SetWindowPos(game, IntPtr.Zero, wx, wy, ww, wh, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOCOPYBITS);
    }

    public override (int Width, int Height) Size(IntPtr box)
    {
        GetClientRect(box, out var r);
        return (r.R - r.L, r.B - r.T);
    }

    public override void Focus(IntPtr game) => Do(game, () => FocusNow(game));

    static void FocusNow(IntPtr game)
    {
        uint theirs = GetWindowThreadProcessId(game, out _), mine = GetCurrentThreadId();
        // SetFocus only works within one input queue – the emulator's thread joins ours for that moment
        AttachThreadInput(mine, theirs, true);
        SetFocus(game);
        AttachThreadInput(mine, theirs, false);
    }

    /// <summary>Waits for it (up to 3 s – MonHub may be about to close), so the window is out before the place goes.</summary>
    public override void Release(IntPtr game, IntPtr box)
    {
        if (_watched == game) _watched = IntPtr.Zero;
        bool foreground = GetForegroundWindow() == GetAncestor(box, 2 /* GA_ROOT */);
        GetWindowRect(box, out var where);
        using var done = new ManualResetEventSlim();
        Do(game, () =>
        {
            try { ReleaseNow(game, where, foreground); }
            finally { done.Set(); }
        });
        done.Wait(TimeSpan.FromSeconds(3));
    }

    void ReleaseNow(IntPtr game, RECT where, bool foreground)
    {
        var (style, ex) = _own.TryGetValue(game, out var own) ? own : (0x14CF0000L /* WS_OVERLAPPEDWINDOW | WS_VISIBLE */, 0L);
        _own.Remove(game);
        _frame.Remove(game);
        SetParent(game, IntPtr.Zero);
        SetWindowLongPtr(game, GWL_STYLE, new IntPtr(style & ~WS_MAXIMIZE));
        SetWindowLongPtr(game, GWL_EXSTYLE, new IntPtr(ex));
        // where the game was, with the frame around it
        var frame = new RECT { L = where.L, T = where.T, R = where.R, B = where.B };
        AdjustWindowRectEx(ref frame, (uint)style, GetMenu(game) != IntPtr.Zero, (uint)ex);
        SetWindowPos(game, IntPtr.Zero, frame.L, Math.Max(frame.T, where.T - 40), frame.R - frame.L, frame.B - frame.T, SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        DrawMenuBar(game);
        // MonHub in front (the button was just clicked): the keys go along to the game's window – never otherwise
        if (foreground) SetForegroundWindow(game);
    }

    public override void Close(IntPtr game) => PostMessage(game, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);

    /// <summary>A child window moved along with its parent gets no WM_MOVE – one step aside and back gives it two.</summary>
    public override void Moved(IntPtr game) => Do(game, () => MovedNow(game));

    static void MovedNow(IntPtr game)
    {
        if (!GetWindowRect(game, out var r) || GetParent(game) is var parent && parent == IntPtr.Zero) return;
        var p = new POINT { X = r.L, Y = r.T };
        ScreenToClient(parent, ref p);
        const uint keep = SWP_NOZORDER | SWP_NOACTIVATE | 0x1 /* SWP_NOSIZE */;
        SetWindowPos(game, IntPtr.Zero, p.X + 1, p.Y, 0, 0, keep);
        SetWindowPos(game, IntPtr.Zero, p.X, p.Y, 0, 0, keep);
    }

    public override bool Exists(IntPtr game) => IsWindow(game);

    // another program's title is kept by the system – reading it asks nothing of the (maybe busy) program
    public override string? Title(IntPtr game)
    {
        var text = new System.Text.StringBuilder(256);
        return GetWindowText(game, text, 256) > 0 ? text.ToString() : null;
    }

    public override (int X, int Y)? Cursor() => GetCursorPos(out var p) ? (p.X, p.Y) : null;

    protected override List<(int X, int Y, int Width, int Height)> Children(IntPtr game, out int width, out int height)
    {
        var list = new List<(int, int, int, int)>();
        GetClientRect(game, out var client);
        width = client.R - client.L;
        height = client.B - client.T;
        for (var child = GetWindow(game, 5 /* GW_CHILD */); child != IntPtr.Zero; child = GetWindow(child, 2 /* GW_HWNDNEXT */))
        {
            if (!IsWindowVisible(child) || !GetWindowRect(child, out var r)) continue;
            var p = new POINT { X = r.L, Y = r.T };
            ScreenToClient(game, ref p);
            list.Add((p.X, p.Y, r.R - r.L, r.B - r.T));
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, System.Text.StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles,
        uint flags, IntPtr environment, string currentDirectory, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int hh, uint flags);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder b, int n);
    [DllImport("user32.dll")] static extern bool AdjustWindowRectEx(ref RECT r, uint style, bool menu, uint ex);
    [DllImport("user32.dll")] static extern IntPtr GetMenu(IntPtr h);
    [DllImport("user32.dll")] static extern bool DrawMenuBar(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder b, int n);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h, IntPtr region, bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}

sealed class X11GameWindow : GameWindow
{
    readonly IntPtr _display;
    readonly IntPtr _root, _pidAtom, _stateAtom, _transientAtom, _protocolsAtom, _deleteAtom, _activeAtom, _cardinal;

    X11GameWindow(IntPtr display)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
        _pidAtom = XInternAtom(display, "_NET_WM_PID", false);
        _stateAtom = XInternAtom(display, "WM_STATE", false);
        _transientAtom = XInternAtom(display, "WM_TRANSIENT_FOR", false);
        _protocolsAtom = XInternAtom(display, "WM_PROTOCOLS", false);
        _deleteAtom = XInternAtom(display, "WM_DELETE_WINDOW", false);
        _activeAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        _cardinal = XInternAtom(display, "CARDINAL", false);
    }

    /// <summary>
    /// A connection of its own to the X server (window ids are the same for every client). An X error – a window gone a
    /// moment later – lands in Avalonia's error handler, which only notes it.
    /// </summary>
    public static X11GameWindow? TryCreate()
    {
        try
        {
            var display = XOpenDisplay(null);
            return display == IntPtr.Zero ? null : new X11GameWindow(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public override IntPtr Find(IReadOnlyCollection<int> pids)
    {
        // the window manager puts every program window into a frame of its own: the client is somewhere below the root
        var stack = new Stack<(IntPtr Window, int Depth)>();
        stack.Push((_root, 0));
        while (stack.Count > 0)
        {
            var (window, depth) = stack.Pop();
            if (window != _root && Pid(window) is int pid && pids.Contains(pid) && HasProperty(window, _stateAtom) && !HasProperty(window, _transientAtom)
                && XGetWindowAttributes(_display, window, out var a) != 0 && a.map_state == 2 /* IsViewable */ && a.width > 100 && a.height > 80)
                return window;
            if (depth >= 3) continue;
            if (XQueryTree(_display, window, out _, out _, out var children, out uint count) == 0) continue;
            for (int i = 0; i < count; i++) stack.Push((Marshal.ReadIntPtr(children, i * IntPtr.Size), depth + 1));
            if (children != IntPtr.Zero) XFree(children);
        }
        return IntPtr.Zero;
    }

    IntPtr Parent(IntPtr window)
    {
        if (XQueryTree(_display, window, out _, out var parent, out var children, out _) == 0) return IntPtr.Zero;
        if (children != IntPtr.Zero) XFree(children);
        return parent;
    }

    int? Pid(IntPtr window)
    {
        if (XGetWindowProperty(_display, window, _pidAtom, 0, 1, false, _cardinal, out _, out int format, out var items, out _, out var data) != 0 || data == IntPtr.Zero) return null;
        try
        {
            return items == 1 && format == 32 ? (int)Marshal.ReadIntPtr(data).ToInt64() : null; // format 32 = longs in memory
        }
        finally
        {
            XFree(data);
        }
    }

    bool HasProperty(IntPtr window, IntPtr atom)
    {
        if (XGetWindowProperty(_display, window, atom, 0, 1, false, IntPtr.Zero /* AnyPropertyType */, out var type, out _, out _, out _, out var data) != 0) return false;
        if (data != IntPtr.Zero) XFree(data);
        return type != IntPtr.Zero;
    }

    public override void Take(IntPtr game, IntPtr box)
    {
        // out of the window manager's hands: unmapped, it lets go of the frame; under our box it never sees it again
        XUnmapWindow(_display, game);
        XSync(_display, false);
        Thread.Sleep(50);
        var (w, h) = Size(box);
        XReparentWindow(_display, game, box, 0, 0);
        XMoveResizeWindow(_display, game, 0, 0, (uint)Math.Max(1, w), (uint)Math.Max(1, h));
        XMapWindow(_display, game);
        XSync(_display, false);
    }

    public override void Place(IntPtr game, int x, int y, int width, int height)
    {
        if (XGetWindowAttributes(_display, game, out var a) != 0 && a.x == x && a.y == y && a.width == width && a.height == height) return;
        XMoveResizeWindow(_display, game, x, y, (uint)Math.Max(1, width), (uint)Math.Max(1, height));
        XFlush(_display);
    }

    public override (int Width, int Height) Size(IntPtr box) =>
        XGetWindowAttributes(_display, box, out var a) != 0 ? (a.width, a.height) : (0, 0);

    public override void Focus(IntPtr game)
    {
        if (XGetWindowAttributes(_display, game, out var a) == 0 || a.map_state != 2) return;
        XSetInputFocus(_display, game, 2 /* RevertToParent */, IntPtr.Zero /* CurrentTime */);
        XFlush(_display);
    }

    /// <summary>Whether the keys go to the game right now.</summary>
    public bool HasFocus(IntPtr game)
    {
        XGetInputFocus(_display, out var focus, out _);
        return focus == game;
    }

    public override void Release(IntPtr game, IntPtr box)
    {
        XTranslateCoordinates(_display, box, _root, 0, 0, out int x, out int y, out _);
        XUnmapWindow(_display, game);
        XSync(_display, false);
        XReparentWindow(_display, game, _root, x, y);
        XMapWindow(_display, game); // a window of the root again: the window manager frames it
        XSync(_display, false);
        // in front of MonHub, with the keys (GNOME would put it behind): asked of the window manager the EWMH way
        Thread.Sleep(100);
        var e = new XClientMessageEvent
        {
            type = 33 /* ClientMessage */, window = game, message_type = _activeAtom, format = 32,
            l0 = (IntPtr)2 /* from a pager-like tool: honoured */, l1 = IntPtr.Zero,
        };
        XSendEvent(_display, _root, false, (IntPtr)(0x80000 | 0x100000) /* SubstructureNotify | SubstructureRedirect */, ref e);
        XFlush(_display);
    }

    public override void Close(IntPtr game)
    {
        // the window manager's close: WM_DELETE_WINDOW, which every program here understands
        var e = new XClientMessageEvent
        {
            type = 33 /* ClientMessage */, window = game, message_type = _protocolsAtom, format = 32,
            l0 = _deleteAtom, l1 = IntPtr.Zero,
        };
        XSendEvent(_display, game, false, IntPtr.Zero, ref e);
        XFlush(_display);
    }

    public override bool Exists(IntPtr game)
    {
        XSync(_display, false);
        return XGetWindowAttributes(_display, game, out _) != 0;
    }

    public override (int X, int Y)? Cursor() =>
        XQueryPointer(_display, _root, out _, out _, out int x, out int y, out _, out _, out _) ? (x, y) : null;

    public override string? Title(IntPtr game)
    {
        if (XFetchName(_display, game, out var name) == 0 || name == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(name);
        }
        finally
        {
            XFree(name);
        }
    }

    /// <summary>An empty shape makes the place (and the game inside) invisible; no shape shows it again.</summary>
    public override void Mask(IntPtr box, bool hidden)
    {
        if (box == IntPtr.Zero) return;
        try
        {
            // the place and the windows Avalonia holds it in – up to (not including) MonHub's own window, the managed one
            for (var w = box; w != IntPtr.Zero && w != _root && !HasProperty(w, _stateAtom); w = Parent(w))
            {
                if (hidden) XShapeCombineRectangles(_display, w, 0 /* ShapeBounding */, 0, 0, IntPtr.Zero, 0, 0 /* ShapeSet */, 0);
                else XShapeCombineMask(_display, w, 0 /* ShapeBounding */, 0, 0, IntPtr.Zero /* None */, 0 /* ShapeSet */);
            }
            XFlush(_display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // no shape extension: the game simply shows while it loads
        }
    }

    protected override List<(int X, int Y, int Width, int Height)> Children(IntPtr game, out int width, out int height)
    {
        var list = new List<(int, int, int, int)>();
        width = height = 0;
        if (XGetWindowAttributes(_display, game, out var own) == 0) return list;
        width = own.width;
        height = own.height;
        if (XQueryTree(_display, game, out _, out _, out var children, out uint count) == 0) return list;
        for (int i = 0; i < count; i++)
        {
            var child = Marshal.ReadIntPtr(children, i * IntPtr.Size);
            if (XGetWindowAttributes(_display, child, out var a) != 0 && a.map_state == 2) list.Add((a.x, a.y, a.width, a.height));
        }
        if (children != IntPtr.Zero) XFree(children);
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XWindowAttributes
    {
        public int x, y, width, height, border_width, depth;
        public IntPtr visual, root;
        public int @class, bit_gravity, win_gravity, backing_store;
        public UIntPtr backing_planes, backing_pixel;
        public int save_under;
        public IntPtr colormap;
        public int map_installed, map_state;
        public IntPtr all_event_masks, your_event_mask, do_not_propagate_mask;
        public int override_redirect;
        public IntPtr screen;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XClientMessageEvent
    {
        public int type;
        public UIntPtr serial;
        public int send_event;
        public IntPtr display, window, message_type;
        public int format;
        public IntPtr l0, l1, l2, l3, l4;
        // XEvent is 24 longs – the rest stays zero
        public IntPtr pad0, pad1, pad2, pad3, pad4, pad5, pad6, pad7, pad8, pad9, pad10, pad11, pad12;
    }

    const string X = "libX11.so.6";
    [DllImport(X)] static extern IntPtr XOpenDisplay(string? name);
    [DllImport(X)] static extern IntPtr XDefaultRootWindow(IntPtr d);
    [DllImport(X)] static extern IntPtr XInternAtom(IntPtr d, string name, bool onlyIfExists);
    [DllImport(X)] static extern int XQueryTree(IntPtr d, IntPtr w, out IntPtr root, out IntPtr parent, out IntPtr children, out uint count);
    [DllImport(X)] static extern int XFree(IntPtr data);
    [DllImport(X)] static extern int XGetWindowProperty(IntPtr d, IntPtr w, IntPtr property, long offset, long length, bool delete, IntPtr reqType,
        out IntPtr actualType, out int actualFormat, out ulong items, out ulong bytesAfter, out IntPtr data);
    [DllImport(X)] static extern int XGetWindowAttributes(IntPtr d, IntPtr w, out XWindowAttributes a);
    [DllImport(X)] static extern int XUnmapWindow(IntPtr d, IntPtr w);
    [DllImport(X)] static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(X)] static extern int XReparentWindow(IntPtr d, IntPtr w, IntPtr parent, int x, int y);
    [DllImport(X)] static extern int XMoveResizeWindow(IntPtr d, IntPtr w, int x, int y, uint width, uint height);
    [DllImport(X)] static extern int XSetInputFocus(IntPtr d, IntPtr w, int revertTo, IntPtr time);
    [DllImport(X)] static extern int XGetInputFocus(IntPtr d, out IntPtr focus, out int revertTo);
    [DllImport(X)] static extern bool XTranslateCoordinates(IntPtr d, IntPtr src, IntPtr dest, int x, int y, out int dx, out int dy, out IntPtr child);
    [DllImport(X)] static extern int XSendEvent(IntPtr d, IntPtr w, bool propagate, IntPtr mask, ref XClientMessageEvent e);
    [DllImport(X)] static extern bool XQueryPointer(IntPtr d, IntPtr w, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int x, out int y, out uint mask);
    [DllImport(X)] static extern int XSync(IntPtr d, bool discard);
    [DllImport(X)] static extern int XFetchName(IntPtr d, IntPtr w, out IntPtr name);
    [DllImport("libXext.so.6")] static extern void XShapeCombineRectangles(IntPtr d, IntPtr w, int kind, int x, int y, IntPtr rects, int count, int op, int ordering);
    [DllImport("libXext.so.6")] static extern void XShapeCombineMask(IntPtr d, IntPtr w, int kind, int x, int y, IntPtr mask, int op);
    [DllImport(X)] static extern int XFlush(IntPtr d);
}
