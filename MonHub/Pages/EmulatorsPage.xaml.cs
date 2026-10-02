using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace MonHub;

/// <summary>One action in the controls editor: what it is, its key and its controller button.</summary>
public record BindingRow(string Id, string Label, string KeyText, string PadText);

/// <summary>"Emulatoren": melonDS and DeSmuME – open them, pick the one new runs start in, and the keyboard layout.</summary>
public partial class EmulatorsPage : UserControl, IHubPage
{
    static readonly double[] FastSpeeds = [2, 3, 4, 5, 6, 8];
    static readonly double[] ToggleSpeeds = [1.5, 2, 2.5, 3];

    ControlSettings _edit = new();
    bool _dirty, _loadingSpeeds;
    string? _listenKey, _listenPad, _padAtStart;
    DispatcherTimer? _padTimer;
    DateTime _padUntil;

    public EmulatorsPage()
    {
        InitializeComponent();
        CmbFast.ItemsSource = FastSpeeds.Select(Speed).ToList();
        CmbToggle.ItemsSource = ToggleSpeeds.Select(Speed).ToList();
        // the key goes to the window: clicking a field rebuilds the list, so the clicked button – and with it the
        // keyboard focus – is gone, and the next key would not pass through this page anymore
        Window? window = null;
        Loaded += (_, _) =>
        {
            window = Window.GetWindow(this);
            if (window != null) window.PreviewKeyDown += CaptureKey;
        };
        Unloaded += (_, _) =>
        {
            if (window != null) window.PreviewKeyDown -= CaptureKey;
            _listenKey = null;
        };
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    static string Speed(double factor) => factor.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + "×";

    public void Refresh()
    {
        MelonIcon.Source = IconHelper.Get(HubPaths.MelonDSExe);
        DesmumeIcon.Source = IconHelper.Get(HubPaths.DeSmuMEExe);
        bool melon = HubPaths.MelonDSExe != null, desmume = HubPaths.DeSmuMEExe != null;
        var main = GameLibrary.MainEmulator();

        TxtMelonStatus.Text = melon ? Status(HubPaths.MelonDSSaves, "*.sav") : Missing;
        TxtDesmumeStatus.Text = desmume ? Status(HubPaths.DeSmuMESaves, "*.dsv") : Missing;
        BtnMelonOpen.IsEnabled = melon;
        BtnDesmumeOpen.IsEnabled = desmume;
        MgbaIcon.Source = IconHelper.Get(HubPaths.MGBAExe);
        BtnMgbaOpen.IsEnabled = HubPaths.MGBAExe != null;
        TxtMgbaStatus.Text = HubPaths.MGBAExe != null ? Status(HubPaths.MGBASaves, "*.sav") : Missing;
        AzaharIcon.Source = IconHelper.Get(HubPaths.AzaharExe);
        BtnAzaharOpen.IsEnabled = HubPaths.AzaharExe != null;
        _loadingSpeeds = true;
        Chk60Fps.IsChecked = HubConfig.Current.Azahar60Fps;
        _loadingSpeeds = false;
        TxtAzaharStatus.Text = HubPaths.AzaharExe != null ? Status(HubPaths.AzaharSaves, "main", recursive: true) : Missing;
        MelonBadge.Visibility = melon && main == GameLibrary.MelonDS ? Visibility.Visible : Visibility.Collapsed;
        DesmumeBadge.Visibility = desmume && main == GameLibrary.DeSmuME ? Visibility.Visible : Visibility.Collapsed;
        BtnMelonDefault.Visibility = melon && main != GameLibrary.MelonDS ? Visibility.Visible : Visibility.Collapsed;
        BtnDesmumeDefault.Visibility = desmume && main != GameLibrary.DeSmuME ? Visibility.Visible : Visibility.Collapsed;

        bool bios = Bios.Installed;
        TxtBiosTitle.Text = bios ? Txt.L("Eigenes DS-BIOS: eingerichtet", "Own DS BIOS: set up") : Txt.L("Eigenes DS-BIOS (optional)", "Own DS BIOS (optional)");
        TxtBios.Text = bios
            ? Txt.L("melonDS startet damit auch verschlüsselte ROMs (z. B. US-Versionen).", "With it, melonDS starts encrypted ROMs too (e.g. US versions).")
            : Txt.L("Deine eigenen Dumps (bios7.bin, bios9.bin, firmware.bin – einzeln oder als ZIP). Damit startet melonDS auch verschlüsselte ROMs; " +
                    "ohne laufen die in DeSmuME. MonHub bringt kein BIOS mit.",
                    "Your own dumps (bios7.bin, bios9.bin, firmware.bin – one by one or as a ZIP). With them, melonDS starts encrypted ROMs too; " +
                    "without them those run in DeSmuME. MonHub doesn't include a BIOS.");
        BtnBiosRemove.Visibility = bios ? Visibility.Visible : Visibility.Collapsed;

        if (!_dirty) _edit = Copy(HubConfig.Current.Controls ?? new ControlSettings()); // unsaved changes survive a refresh
        TxtPad.Text = ControlSettings.ConnectedPad() is { } pad ? Txt.L($"Controller {pad + 1} verbunden", $"Controller {pad + 1} connected") : Txt.L("Kein Controller verbunden", "No controller connected");
        ShowControls();
    }

    static ControlSettings Copy(ControlSettings c) => new()
    {
        Keys = new(c.Keys), Pad = new(c.Pad), FastForward = c.FastForward, Toggle = c.Toggle,
    };

    /// <summary>One input does one thing: taken by another field, it is cleared there.</summary>
    static void Assign<T>(Dictionary<string, T> map, string action, T value, T none)
    {
        foreach (var other in map.Where(e => e.Key != action && EqualityComparer<T>.Default.Equals(e.Value, value)).Select(e => e.Key).ToList())
            map[other] = none;
        map[action] = value;
    }

    /// <summary>Right click: the field is emptied (the input then does nothing).</summary>
    void KeyClear_Click(object sender, MouseButtonEventArgs e)
    {
        _edit.Keys[(string)((FrameworkElement)sender).Tag] = -1;
        _dirty = true;
        e.Handled = true;
        ShowControls();
    }

    void PadClear_Click(object sender, MouseButtonEventArgs e)
    {
        StopPad();
        _edit.Pad[(string)((FrameworkElement)sender).Tag] = "";
        _dirty = true;
        e.Handled = true;
        ShowControls();
    }

    void ShowControls()
    {
        BindingList.ItemsSource = ControlSettings.Actions.Select(a => new BindingRow(a.Id, a.Label,
            _listenKey == a.Id ? Txt.L("Taste …", "Key …") : ControlSettings.KeyLabel(_edit.Keys.GetValueOrDefault(a.Id, -1)),
            _listenPad == a.Id ? Txt.L("Knopf …", "Button …") : ControlSettings.PadLabel(_edit.Pad.GetValueOrDefault(a.Id, "")))).ToList();
        _loadingSpeeds = true;
        CmbFast.SelectedItem = Speed(_edit.FastForward);
        CmbToggle.SelectedItem = Speed(_edit.Toggle);
        _loadingSpeeds = false;

        // the same key or button for two things would do both at once
        var doubleKeys = _edit.Keys.Where(k => k.Value > 0).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => ControlSettings.KeyLabel(g.Key));
        var doublePads = _edit.Pad.Where(p => p.Value.Length > 0).GroupBy(p => p.Value).Where(g => g.Count() > 1).Select(g => ControlSettings.PadLabel(g.Key));
        var doubles = doubleKeys.Concat(doublePads).ToList();
        TxtControls.Text = doubles.Count > 0 ? Txt.L("⚠ Doppelt belegt: ", "⚠ Used twice: ") + string.Join(", ", doubles)
                         : _dirty ? Txt.L("Geändert – mit „Übernehmen“ speichern.", "Changed – save with “Apply”.") : "";
        BtnControlsApply.IsEnabled = _dirty;
    }

    void Key_Click(object sender, RoutedEventArgs e)
    {
        StopPad();
        _listenKey = (string)((FrameworkElement)sender).Tag;
        ShowControls();
    }

    /// <summary>The next key after clicking a keyboard field (the page sees it before the button does).</summary>
    void CaptureKey(object sender, KeyEventArgs e)
    {
        if (_listenKey == null || !IsVisible) return; // another page is shown: the key is for it
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape)
        {
            if (ControlSettings.QtCode(key) is not { } code) return; // F11, Windows … stay reserved – keep listening
            Assign(_edit.Keys, _listenKey, code, -1);
            _dirty = true;
        }
        _listenKey = null;
        ShowControls();
    }

    void Pad_Click(object sender, RoutedEventArgs e)
    {
        _listenKey = null;
        if (ControlSettings.ConnectedPad() == null)
        {
            TxtControls.Text = Txt.L("Kein Controller gefunden – anschließen oder einschalten, dann nochmal klicken.",
                "No controller found – plug it in or switch it on, then click again.");
            return;
        }
        _listenPad = (string)((FrameworkElement)sender).Tag;
        _padAtStart = ControlSettings.PressedPad(); // a button still held (or a stick still pushed) from before doesn't count
        _padUntil = DateTime.Now.AddSeconds(6);
        _padTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Input, (_, _) => PollPad(), Dispatcher);
        _padTimer.Start();
        ShowControls();
    }

    void PollPad()
    {
        var pressed = ControlSettings.PressedPad();
        if (pressed == _padAtStart && pressed != null) return;
        _padAtStart = null;
        if (pressed != null && _listenPad != null)
        {
            Assign(_edit.Pad, _listenPad, pressed, "");
            _dirty = true;
            StopPad();
        }
        else if (DateTime.Now > _padUntil) StopPad();
    }

    void StopPad()
    {
        _padTimer?.Stop();
        _listenPad = null;
        ShowControls();
    }

    void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSpeeds || CmbFast.SelectedIndex < 0 || CmbToggle.SelectedIndex < 0) return;
        _edit.FastForward = FastSpeeds[CmbFast.SelectedIndex];
        _edit.Toggle = ToggleSpeeds[CmbToggle.SelectedIndex];
        _dirty = true;
        ShowControls();
    }

    void ControlsDefault_Click(object sender, RoutedEventArgs e)
    {
        _edit = new ControlSettings();
        _dirty = true;
        ShowControls();
    }

    /// <summary>Saves and writes into melonDS and mGBA – an open emulator gets it as soon as it is closed.</summary>
    void ControlsApply_Click(object sender, RoutedEventArgs e)
    {
        HubConfig.Current.Controls = Copy(_edit);
        HubConfig.Current.Save();
        _dirty = false;
        var open = new[] { ("melonDS", "melonDS"), ("mGBA", "mGBA"), ("azahar", "Azahar") }.Where(x => HubSetup.IsRunning(x.Item1)).Select(x => x.Item2).ToList();
        HubSetup.EnsureAll();
        ShowControls();
        TxtControls.Text = open.Count == 0 ? Txt.L("✓ Übernommen – gilt beim nächsten Spielstart.", "✓ Applied – takes effect at the next game start.")
            : Txt.L($"Gespeichert – {string.Join(" und ", open)} {(open.Count == 1 ? "ist" : "sind")} gerade offen und bekommt es, sobald du es schließt.",
                    $"Saved – {string.Join(" and ", open)} {(open.Count == 1 ? "is" : "are")} open right now and {(open.Count == 1 ? "gets" : "get")} it once you close {(open.Count == 1 ? "it" : "them")}.");
    }

    void BiosImport_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = Txt.L("Eigene BIOS-Dateien wählen (ZIP oder bios7.bin, bios9.bin, firmware.bin)", "Choose your own BIOS files (ZIP or bios7.bin, bios9.bin, firmware.bin)"),
            Filter = Txt.L("BIOS-Dateien", "BIOS files") + " (*.zip;*.bin)|*.zip;*.bin",
            Multiselect = true,
        };
        if (dlg.ShowDialog(owner) != true) return;
        List<string> taken;
        try
        {
            taken = Bios.Import(dlg.FileNames);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, Txt.L("Die Dateien konnten nicht gelesen werden: ", "The files couldn't be read: ") + ex.Message, "BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string text = taken.Count == 0
            ? Txt.L("Keine passenden Dateien gefunden. Gesucht: bios7.bin (16 KB), bios9.bin (4 KB), firmware.bin (128–512 KB).",
                    "No matching files found. Looking for: bios7.bin (16 KB), bios9.bin (4 KB), firmware.bin (128–512 KB).")
            : Bios.Installed ? Txt.L("Eingerichtet – melonDS startet jetzt auch verschlüsselte ROMs.", "Set up – melonDS now starts encrypted ROMs too.")
            : Txt.L("Übernommen: ", "Taken: ") + string.Join(", ", taken) + Txt.L(". Es fehlen noch: ", ". Still missing: ") + string.Join(", ", Bios.Missing) + ".";
        if (Bios.Installed && !HubSetup.ApplyBiosToMelonDS())
            text += Environment.NewLine + Environment.NewLine + Txt.L("melonDS läuft gerade – beim nächsten Start von MonHub (mit geschlossenem melonDS) wird es eingetragen.",
                "melonDS is running right now – it gets set up the next time MonHub starts (with melonDS closed).");
        MessageBox.Show(owner, text, "BIOS", MessageBoxButton.OK, MessageBoxImage.Information);
        Refresh();
    }

    void BiosRemove_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (HubSetup.IsRunning("melonDS"))
        {
            MessageBox.Show(owner, Txt.L("Bitte schließ zuerst melonDS.", "Please close melonDS first."), "BIOS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Bios.Remove();
        HubSetup.ApplyBiosToMelonDS();
        Refresh();
    }

    static string Status(string saves, string pattern, bool recursive = false)
    {
        int count;
        try { count = Directory.Exists(saves) ? Directory.GetFiles(saves, pattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Length : 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { count = 0; } // folder busy or locked: count later
        return count switch
        {
            0 => Txt.L("Bereit · noch keine Spielstände", "Ready · no saves yet"),
            1 => Txt.L("Bereit · 1 Spielstand", "Ready · 1 save"),
            _ => Txt.L($"Bereit · {count} Spielstände", $"Ready · {count} saves"),
        };
    }

    static string Missing => Txt.L("Nicht gefunden – bitte MonHub neu installieren.", "Not found – please reinstall MonHub.");

    void MgbaOpen_Click(object sender, RoutedEventArgs e) => Launcher.Start(Window.GetWindow(this), HubPaths.MGBAExe);
    void MgbaSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.MGBASaves);
    void Fps_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingSpeeds) return;
        HubConfig.Current.Azahar60Fps = Chk60Fps.IsChecked == true;
        HubConfig.Current.Save();
    }

    void AzaharOpen_Click(object sender, RoutedEventArgs e) => Launcher.Start(Window.GetWindow(this), HubPaths.AzaharExe);
    void AzaharSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.AzaharSaves);
    void MelonOpen_Click(object sender, RoutedEventArgs e) => Launcher.Start(Window.GetWindow(this), HubPaths.MelonDSExe);
    void DesmumeOpen_Click(object sender, RoutedEventArgs e) => Launcher.Start(Window.GetWindow(this), HubPaths.DeSmuMEExe);
    void MelonSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.MelonDSSaves);
    void DesmumeSaves_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.DeSmuMESaves);
    void MelonDefault_Click(object sender, RoutedEventArgs e) => SetDefault(GameLibrary.MelonDS);
    void DesmumeDefault_Click(object sender, RoutedEventArgs e) => SetDefault(GameLibrary.DeSmuME);

    /// <summary>"Als Standard": the emulator new runs start in.</summary>
    void SetDefault(string emulator)
    {
        var owner = Window.GetWindow(this);
        try
        {
            HubSetup.SetMainEmulator(emulator);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Standard-Emulator", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Refresh();
    }

    /// <summary>Narrow window: the two emulators below each other.</summary>
    void Arrange(double width)
    {
        bool narrow = width < 780;
        EmuSecond.Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        EmuGap.Width = narrow ? new GridLength(0) : new GridLength(18);
        Grid.SetColumn(DesmumePanel, narrow ? 0 : 2);
        Grid.SetRow(DesmumePanel, narrow ? 1 : 0);
        DesmumePanel.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(0);
    }
}
