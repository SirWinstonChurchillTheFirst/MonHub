using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace PokeHub;

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
        PreviewKeyDown += CaptureKey;
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    static string Speed(double factor) => factor.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + "×";

    public void Refresh()
    {
        MelonIcon.Source = IconHelper.Get(HubPaths.MelonDSExe);
        DesmumeIcon.Source = IconHelper.Get(HubPaths.DeSmuMEExe);
        bool melon = HubPaths.MelonDSExe != null, desmume = HubPaths.DeSmuMEExe != null;
        var main = GameLibrary.MainEmulator();

        TxtMelonStatus.Text = melon ? Status(HubPaths.MelonDSSaves, "*.sav") : "Nicht gefunden – bitte MonHub neu installieren.";
        TxtDesmumeStatus.Text = desmume ? Status(HubPaths.DeSmuMESaves, "*.dsv") : "Nicht gefunden – bitte MonHub neu installieren.";
        BtnMelonOpen.IsEnabled = melon;
        BtnDesmumeOpen.IsEnabled = desmume;
        MgbaIcon.Source = IconHelper.Get(HubPaths.MGBAExe);
        BtnMgbaOpen.IsEnabled = HubPaths.MGBAExe != null;
        TxtMgbaStatus.Text = HubPaths.MGBAExe != null ? Status(HubPaths.MGBASaves, "*.sav") : "Nicht gefunden – bitte MonHub neu installieren.";
        AzaharIcon.Source = IconHelper.Get(HubPaths.AzaharExe);
        BtnAzaharOpen.IsEnabled = HubPaths.AzaharExe != null;
        _loadingSpeeds = true;
        Chk60Fps.IsChecked = HubConfig.Current.Azahar60Fps;
        _loadingSpeeds = false;
        TxtAzaharStatus.Text = HubPaths.AzaharExe != null ? Status(HubPaths.AzaharSaves, "main", recursive: true) : "Nicht gefunden – bitte MonHub neu installieren.";
        MelonBadge.Visibility = melon && main == GameLibrary.MelonDS ? Visibility.Visible : Visibility.Collapsed;
        DesmumeBadge.Visibility = desmume && main == GameLibrary.DeSmuME ? Visibility.Visible : Visibility.Collapsed;
        BtnMelonDefault.Visibility = melon && main != GameLibrary.MelonDS ? Visibility.Visible : Visibility.Collapsed;
        BtnDesmumeDefault.Visibility = desmume && main != GameLibrary.DeSmuME ? Visibility.Visible : Visibility.Collapsed;

        bool bios = Bios.Installed;
        TxtBiosTitle.Text = bios ? "Eigenes DS-BIOS: eingerichtet" : "Eigenes DS-BIOS (optional)";
        TxtBios.Text = bios
            ? "melonDS startet damit auch verschlüsselte ROMs (z. B. US-Versionen)."
            : "Deine eigenen Dumps (bios7.bin, bios9.bin, firmware.bin – einzeln oder als ZIP). Damit startet melonDS auch verschlüsselte ROMs; " +
              "ohne laufen die in DeSmuME. MonHub bringt kein BIOS mit.";
        BtnBiosRemove.Visibility = bios ? Visibility.Visible : Visibility.Collapsed;

        if (!_dirty) _edit = Copy(HubConfig.Current.Controls ?? new ControlSettings()); // unsaved changes survive a refresh
        TxtPad.Text = ControlSettings.ConnectedPad() is { } pad ? $"Controller {pad + 1} verbunden" : "Kein Controller verbunden";
        ShowControls();
    }

    static ControlSettings Copy(ControlSettings c) => new()
    {
        Keys = new(c.Keys), Pad = new(c.Pad), FastForward = c.FastForward, Toggle = c.Toggle,
    };

    void ShowControls()
    {
        BindingList.ItemsSource = ControlSettings.Actions.Select(a => new BindingRow(a.Id, a.Label,
            _listenKey == a.Id ? "Taste …" : ControlSettings.KeyLabel(_edit.Keys.GetValueOrDefault(a.Id, -1)),
            _listenPad == a.Id ? "Knopf …" : ControlSettings.PadLabel(_edit.Pad.GetValueOrDefault(a.Id, "")))).ToList();
        _loadingSpeeds = true;
        CmbFast.SelectedItem = Speed(_edit.FastForward);
        CmbToggle.SelectedItem = Speed(_edit.Toggle);
        _loadingSpeeds = false;

        // the same key or button for two things would do both at once
        var doubleKeys = _edit.Keys.Where(k => k.Value > 0).GroupBy(k => k.Value).Where(g => g.Count() > 1).Select(g => ControlSettings.KeyLabel(g.Key));
        var doublePads = _edit.Pad.Where(p => p.Value.Length > 0).GroupBy(p => p.Value).Where(g => g.Count() > 1).Select(g => ControlSettings.PadLabel(g.Key));
        var doubles = doubleKeys.Concat(doublePads).ToList();
        TxtControls.Text = doubles.Count > 0 ? "⚠ Doppelt belegt: " + string.Join(", ", doubles)
                         : _dirty ? "Geändert – mit „Übernehmen“ speichern." : "";
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
        if (_listenKey == null) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape)
        {
            if (ControlSettings.QtCode(key) is not { } code) return; // arrows, F11 … stay reserved – keep listening
            _edit.Keys[_listenKey] = code;
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
            TxtControls.Text = "Kein Controller gefunden – anschließen oder einschalten, dann nochmal klicken.";
            return;
        }
        _listenPad = (string)((FrameworkElement)sender).Tag;
        _padAtStart = ControlSettings.PressedPad(); // a button still held from before doesn't count
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
            _edit.Pad[_listenPad] = pressed;
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
        TxtControls.Text = open.Count == 0 ? "✓ Übernommen – gilt beim nächsten Spielstart."
            : $"Gespeichert – {string.Join(" und ", open)} {(open.Count == 1 ? "ist" : "sind")} gerade offen und bekommt es, sobald du es schließt.";
    }

    void BiosImport_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Eigene BIOS-Dateien wählen (ZIP oder bios7.bin, bios9.bin, firmware.bin)",
            Filter = "BIOS-Dateien (*.zip;*.bin)|*.zip;*.bin",
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
            MessageBox.Show(owner, "Die Dateien konnten nicht gelesen werden: " + ex.Message, "BIOS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        string text = taken.Count == 0
            ? "Keine passenden Dateien gefunden. Gesucht: bios7.bin (16 KB), bios9.bin (4 KB), firmware.bin (128–512 KB)."
            : Bios.Installed ? "Eingerichtet – melonDS startet jetzt auch verschlüsselte ROMs."
            : "Übernommen: " + string.Join(", ", taken) + ". Es fehlen noch: " + string.Join(", ", Bios.Missing) + ".";
        if (Bios.Installed && !HubSetup.ApplyBiosToMelonDS())
            text += Environment.NewLine + Environment.NewLine + "melonDS läuft gerade – beim nächsten Start von MonHub (mit geschlossenem melonDS) wird es eingetragen.";
        MessageBox.Show(owner, text, "BIOS", MessageBoxButton.OK, MessageBoxImage.Information);
        Refresh();
    }

    void BiosRemove_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        if (HubSetup.IsRunning("melonDS"))
        {
            MessageBox.Show(owner, "Bitte schließ zuerst melonDS.", "BIOS", MessageBoxButton.OK, MessageBoxImage.Information);
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
            0 => "Bereit · noch keine Spielstände",
            1 => "Bereit · 1 Spielstand",
            _ => $"Bereit · {count} Spielstände",
        };
    }

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
