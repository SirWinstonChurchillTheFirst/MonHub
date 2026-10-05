using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MonHub;

public partial class App : Application
{
    /// <summary>
    /// The system is closing MonHub – log-off, or the setup updating it. From then on no UI work and no message boxes:
    /// a dialog would keep the process alive and the setup could not replace its files.
    /// </summary>
    public static bool ShuttingDown { get; private set; }

    bool _reporting;
    DateTime _lastError;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    static IClassicDesktopStyleApplicationLifetime? Desktop => Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public static void Shutdown(int code = 0)
    {
        ShuttingDown = true;
        Desktop?.Shutdown(code);
    }

    /// <summary>An error on the UI thread: written to the crash log and shown once – MonHub stays open if it can.</summary>
    void OnUiError(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var log = Path.Combine(HubPaths.SettingsDir, "MonHub-crash.log");
        try
        {
            Directory.CreateDirectory(HubPaths.SettingsDir);
            if (_reporting) File.AppendAllText(log, "\n\n" + e.Exception);
            else File.WriteAllText(log, e.Exception.ToString());
        }
        catch
        {
            // nowhere to write – still show the message
        }
        e.Handled = true;
        // an error while the message is open (e.g. one that repeats on every redraw) is only logged: another
        // message would run the same code again, one box inside the other, until the stack overflows
        if (ShuttingDown || _reporting) return;
        if (DateTime.Now - _lastError < TimeSpan.FromSeconds(3))
        {
            Shutdown(1); // the same trouble right after the message was closed: close instead of looping
            return;
        }
        _reporting = true;
        try
        {
            MessageBox.Show(e.Exception.Message + $"\n\nDetails: {log}", Txt.L("MonHub – Fehler", "MonHub – error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _reporting = false;
            _lastError = DateTime.Now;
        }
        if (Desktop?.MainWindow is not { IsVisible: true }) Shutdown(1);
    }

    static bool _menuReplay;
    static Mutex? _single;
    static FileSystemWatcher? _wake;

    /// <summary>Starts MonHub again (e.g. in another language) and closes this one.</summary>
    public static void Restart()
    {
        // the new MonHub must not find this one still "running" and just bring it to the front
        try { _single?.ReleaseMutex(); } catch (ApplicationException) { /* not ours (any more) */ }
        _single?.Dispose();
        _single = null;
        _wake?.Dispose();
        _wake = null;
        if (Environment.ProcessPath is { } exe) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = OperatingSystem.IsWindows() });
        Shutdown();
    }

    static string WakeFile => Path.Combine(HubPaths.SettingsDir, ".show");

    /// <summary>
    /// One MonHub per installation: two would both write the emulators' settings and watch the same folders. A second
    /// start (double-click while it is open, e.g. hidden behind other windows) brings the open one to the front instead:
    /// it touches a small file the open MonHub is watching.
    /// </summary>
    static bool AlreadyRunning()
    {
        var id = "MonHub-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(HubPaths.Root.ToLowerInvariant())))[..16];
        _single = new Mutex(true, id + "-instance", out bool first);
        try
        {
            Directory.CreateDirectory(HubPaths.SettingsDir);
            if (!first)
            {
                File.WriteAllText(WakeFile, DateTime.Now.Ticks.ToString());
                return true;
            }
            _wake = new FileSystemWatcher(HubPaths.SettingsDir, ".show") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
            FileSystemEventHandler show = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (Desktop?.MainWindow is not { } window) return;
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Show();
                window.Activate();
                window.Topmost = true; // the window only comes forward like this …
                window.Topmost = false; // … without staying on top
            });
            _wake.Changed += show;
            _wake.Created += show;
            _wake.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the folder can't be written or watched: still only one MonHub, it just isn't brought forward
        }
        return !first;
    }

    /// <summary>Errors outside the UI thread (background work, forgotten tasks) end up in the crash log too.</summary>
    static void LogBackground(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            Directory.CreateDirectory(HubPaths.SettingsDir);
            File.AppendAllText(Path.Combine(HubPaths.SettingsDir, "MonHub-crash.log"), $"\n\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Hintergrund:\n{ex}");
        }
        catch
        {
            // nowhere to write
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return; // tests and screenshots set things up themselves
        if (AlreadyRunning())
        {
            desktop.Shutdown(0);
            return;
        }
        desktop.ShutdownRequested += (_, _) => ShuttingDown = true;
        desktop.Exit += (_, _) => ShuttingDown = true;
        Dispatcher.UIThread.UnhandledException += OnUiError;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogBackground(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            LogBackground(a.Exception);
            a.SetObserved(); // a lost background task must never take MonHub down
        };
        Prepare();
        desktop.MainWindow = new MainWindow();
    }

    /// <summary>Everything MonHub needs before its first window: settings, language, pictures, the theme.</summary>
    public static void Prepare(bool ensure = true)
    {
        Legacy.MoveSettings(); // before anything reads the settings
        Txt.ApplyCulture();
        Species.Register();
        HubAnim.RegisterButtonPress();
        // a menu entry's action runs a moment later, when the menu has closed: a dialog opened while the menu is
        // still up loses its first click to the menu (which takes it as "clicked elsewhere, close me")
        MenuItem.ClickEvent.AddClassHandler<MenuItem>((item, e) =>
        {
            if (_menuReplay || item.Items.Count > 0) return;
            e.Handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _menuReplay = true;
                try { item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); }
                finally { _menuReplay = false; }
            }, DispatcherPriority.Background);
        });
        var config = HubConfig.Load();
        if (ensure) HubSetup.EnsureAll();
        HubThemes.Apply(config.Theme);
        HubMotion.Apply(config.EffectiveMotion);
    }
}
