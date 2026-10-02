using System.IO;
using System.Windows;

namespace MonHub;

public partial class App : Application
{
    /// <summary>
    /// Windows is closing MonHub – log-off, or the setup updating it. From then on no UI work and no message boxes:
    /// a dialog would keep the process alive and the setup could not replace its files.
    /// </summary>
    public static bool ShuttingDown { get; private set; }

    bool _reporting;
    DateTime _lastError;

    public App()
    {
        SessionEnding += (_, _) => ShuttingDown = true;
        Exit += (_, _) => ShuttingDown = true;
        DispatcherUnhandledException += (_, e) =>
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
            if (MainWindow == null || !MainWindow.IsLoaded) Shutdown(1);
        };
    }

    static Mutex? _single;

    /// <summary>Starts MonHub again (e.g. in another language) and closes this one.</summary>
    public static void Restart()
    {
        // the new MonHub must not find this one still "running" and just bring it to the front
        try { _single?.ReleaseMutex(); } catch (ApplicationException) { /* not ours (any more) */ }
        _single?.Dispose();
        _single = null;
        if (Environment.ProcessPath is { } exe) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        Current.Shutdown();
    }

    /// <summary>
    /// One MonHub per installation: two would both write the emulators' settings and watch the same folders. A second
    /// start (double-click while it is open, e.g. hidden behind other windows) brings the open one to the front instead.
    /// </summary>
    static bool AlreadyRunning()
    {
        var id = "MonHub-" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(HubPaths.Root.ToLowerInvariant())))[..16];
        _single = new Mutex(true, id + "-instance", out bool first);
        var wake = new EventWaitHandle(false, EventResetMode.AutoReset, id + "-show");
        if (!first)
        {
            wake.Set();
            return true;
        }
        var thread = new Thread(() =>
        {
            while (wake.WaitOne())
                Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Current.MainWindow is not { } window) return;
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Show();
                    window.Activate();
                    window.Topmost = true; // Windows only lets the window come forward like this …
                    window.Topmost = false; // … without it staying on top
                });
        }) { IsBackground = true, Name = "MonHub single instance" };
        thread.Start();
        return false;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (AlreadyRunning())
        {
            Shutdown(0);
            return;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, a) => LogBackground(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            LogBackground(a.Exception);
            a.SetObserved(); // a lost background task must never take MonHub down
        };
        // the close button of MonHub's own title bars (Hub.DialogWindow)
        System.Windows.Input.CommandManager.RegisterClassCommandBinding(typeof(Window),
            new System.Windows.Input.CommandBinding(SystemCommands.CloseWindowCommand, (s, _) => SystemCommands.CloseWindow((Window)s)));
        Legacy.MoveSettings(); // before anything reads the settings
        Txt.ApplyCulture();
        Species.Register();
        HubAnim.RegisterButtonPress();
        var config = HubConfig.Load();
        HubSetup.EnsureAll();
        HubThemes.Apply(config.Theme);
        HubMotion.Apply(config.EffectiveMotion);
        var main = new MainWindow();
        MainWindow = main;
        main.Show();
    }
}
