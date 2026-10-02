using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PokeHub;

public partial class ImportWindow : Window
{
    /// <summary>A source with its checkbox state.</summary>
    public class SourceRow
    {
        public required ImportSource Source { get; init; }
        public bool IsChecked { get; set; } = true;
        public string ExtraText => Source.ExtraFolders.Count == 0 ? "" : $"  (+ {Source.ExtraFolders.Count} verknüpfte Ordner mit ROMs/Spielständen)";
    }

    readonly List<SourceRow> _sources = new();
    List<ImportItem> _items = new();
    CancellationTokenSource? _scan;
    bool _busy;

    /// <summary>True when something was imported (the launcher refreshes its counters).</summary>
    public bool ImportedSomething { get; private set; }

    public ImportWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        UpdateButtons();
        Loaded += async (_, _) => await SearchEmulatorsAsync();
        Closed += (_, _) => _scan?.Cancel();
    }

    async Task SearchEmulatorsAsync()
    {
        SearchingPanel.Visibility = Visibility.Visible;
        try
        {
            var found = await Task.Run(() => Importer.FindEmulators());
            foreach (var src in found)
                AddSource(src);
        }
        catch (Exception ex)
        {
            TxtResult.Text = "Suche fehlgeschlagen: " + ex.Message;
        }
        SearchingPanel.Visibility = Visibility.Collapsed;
        TxtNoSources.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        await RescanAsync();
    }

    void AddSource(ImportSource src)
    {
        if (_sources.Any(s => s.Source.Folder.Equals(src.Folder, StringComparison.OrdinalIgnoreCase))) return;
        _sources.Add(new SourceRow { Source = src });
        SourceList.ItemsSource = null;
        SourceList.ItemsSource = _sources;
        TxtNoSources.Visibility = Visibility.Collapsed;
    }

    async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Ordner deines Emulators oder deiner ROMs wählen" };
        if (dlg.ShowDialog(this) != true) return;
        var folder = dlg.FolderName;

        // Emulators inside the chosen folder bring their configured ROM/save folders along
        try
        {
            var exes = Directory.EnumerateFiles(folder, "*.exe", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                .Where(Importer.IsEmulatorExe).ToList();
            foreach (var exe in exes)
                if (Importer.FromEmulatorExe(exe) is { } src) AddSource(src);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an unreadable folder (unplugged drive, no access): take it as a plain folder
        }
        AddSource(new ImportSource("Ordner", folder, new()));
        await RescanAsync();
    }

    async void Source_Toggled(object sender, RoutedEventArgs e) => await RescanAsync();

    void Item_Toggled(object sender, RoutedEventArgs e) => UpdateButtons();

    async Task RescanAsync()
    {
        _scan?.Cancel();
        _scan = new CancellationTokenSource();
        var token = _scan.Token;
        var chosen = _sources.Where(s => s.IsChecked).Select(s => s.Source).ToList();
        TxtSummary.Text = chosen.Count == 0 ? "" : "wird durchsucht …";
        try
        {
            var items = await Task.Run(() => Importer.Scan(chosen, token), token);
            _items = items;
            ItemList.ItemsSource = _items;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            TxtResult.Text = "⚠ Durchsuchen fehlgeschlagen: " + ex.Message;
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Danger");
        }
        UpdateButtons();
    }

    void UpdateButtons()
    {
        int roms = _items.Count(i => i.Kind == ImportKind.Rom);
        int saves = _items.Count - roms;
        int selected = _items.Count(i => i.Selected);
        TxtSummary.Text = _items.Count == 0 ? "noch nichts gefunden" : $"{roms} ROMs · {saves} Spielstände · {selected} ausgewählt";
        TxtImportButton.Text = selected == 0 ? "Importieren" : $"{selected} importieren";
        BtnImport.IsEnabled = selected > 0 && !_busy;
    }

    async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        SourcePanelEnabled(false);
        int total = _items.Count(i => i.Selected), done = 0;
        ImportProgress.Maximum = Math.Max(1, total);
        ImportProgress.Value = 0;
        ImportProgress.Visibility = Ball.Visibility = Visibility.Visible;
        TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Ink");
        HubAnim.Wobble(BallWobble, true);
        var progress = new Progress<string>(name =>
        {
            ImportProgress.Value = Math.Min(total, ++done);
            TxtResult.Text = $"Kopiere {name} …  ({done}/{total})";
        });
        try
        {
            var items = _items.ToList(); // the list may be rescanned meanwhile
            var result = await Task.Run(() => Importer.Import(items, progress));
            ImportedSomething |= result.Copied > 0;
            TxtResult.Text = $"Fertig! {result.Copied} importiert" + (result.Skipped > 0 ? $", {result.Skipped} übersprungen (schon vorhanden)" : "") + "."
                             + (result.Problems.Count > 0 ? $"\n⚠ {result.Problems.Count} Problem(e): " + string.Join("; ", result.Problems.Take(3)) : "");
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, result.Problems.Count > 0 ? "Hub.Danger" : "Hub.Good");
        }
        catch (Exception ex)
        {
            // disk full, drive unplugged … – reported, never a crash; what was copied stays copied
            TxtResult.Text = "⚠ Import abgebrochen: " + ex.Message;
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Danger");
        }
        finally
        {
            _busy = false;
            HubAnim.Wobble(BallWobble, false);
            HubAnim.Click(BallClick);
            ImportProgress.Visibility = Visibility.Collapsed;
            HubAnim.SlideIn(TxtResult);
            SourcePanelEnabled(true);
            UpdateButtons();
        }
        await RescanAsync(); // everything imported now shows "schon im MonHub"
    }

    /// <summary>Sources can't change while copying (a rescan would swap the list under the running import).</summary>
    void SourcePanelEnabled(bool enabled) => SourceList.IsEnabled = ItemList.IsEnabled = enabled;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // closing in the middle of copying would leave the player wondering what arrived – finish first
        if (_busy && !App.ShuttingDown)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Der Import läuft noch – gleich fertig.", "Importieren", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        base.OnClosing(e);
    }
}
