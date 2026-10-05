using System.IO;

namespace MonHub;

public partial class ImportWindow : HubWindow
{
    /// <summary>A source with its checkbox state.</summary>
    public class SourceRow
    {
        public required ImportSource Source { get; init; }
        public bool IsChecked { get; set; } = true;
        public string ExtraText => Source.ExtraFolders.Count == 0 ? "" : Txt.L($"  (+ {Source.ExtraFolders.Count} verknüpfte Ordner mit ROMs/Spielständen)",
            $"  (+ {Source.ExtraFolders.Count} linked {(Source.ExtraFolders.Count == 1 ? "folder" : "folders")} with ROMs/saves)");
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
            TxtResult.Text = Txt.L("Suche fehlgeschlagen: ", "Search failed: ") + ex.Message;
        }
        SearchingPanel.Visibility = Visibility.Collapsed;
        TxtNoSources.Visibility = _sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        await RescanAsync();
    }

    void AddSource(ImportSource src)
    {
        if (_sources.Any(s => s.Source.Folder.Equals(src.Folder, StringComparison.OrdinalIgnoreCase))) return;
        _sources.Add(new SourceRow { Source = src });
        SourceList.ItemsSource = _sources.ToList(); // a new list each time: the control only notices a different one
        TxtNoSources.Visibility = Visibility.Collapsed;
    }

    async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folder = Pick.Folder(this, Txt.L("Ordner deines Emulators oder deiner ROMs wählen", "Choose the folder of your emulator or your ROMs"));
        if (folder == null) return;

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
        AddSource(new ImportSource(Txt.L("Ordner", "Folder"), folder, new()));
        await RescanAsync();
    }

    async void Source_Toggled(object? sender, RoutedEventArgs e) => await RescanAsync();

    void Item_Toggled(object? sender, RoutedEventArgs e) => UpdateButtons();

    async Task RescanAsync()
    {
        _scan?.Cancel();
        _scan = new CancellationTokenSource();
        var token = _scan.Token;
        var chosen = _sources.Where(s => s.IsChecked).Select(s => s.Source).ToList();
        TxtSummary.Text = chosen.Count == 0 ? "" : Txt.L("wird durchsucht …", "searching …");
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
            TxtResult.Text = Txt.L("⚠ Durchsuchen fehlgeschlagen: ", "⚠ Search failed: ") + ex.Message;
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Danger");
        }
        UpdateButtons();
    }

    void UpdateButtons()
    {
        int roms = _items.Count(i => i.Kind == ImportKind.Rom);
        int saves = _items.Count - roms;
        int selected = _items.Count(i => i.Selected);
        TxtSummary.Text = _items.Count == 0 ? Txt.L("noch nichts gefunden", "nothing found yet")
            : Txt.L($"{roms} ROMs · {saves} Spielstände · {selected} ausgewählt", $"{roms} ROMs · {saves} {(saves == 1 ? "save" : "saves")} · {selected} selected");
        TxtImportButton.Text = selected == 0 ? Txt.L("Importieren", "Import") : Txt.L($"{selected} importieren", $"Import {selected}");
        BtnImport.IsEnabled = selected > 0 && !_busy;
    }

    async void Import_Click(object? sender, RoutedEventArgs e)
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
        HubAnim.Wobble(Ball, true);
        var progress = new Progress<string>(name =>
        {
            ImportProgress.Value = Math.Min(total, ++done);
            TxtResult.Text = Txt.L($"Kopiere {name} …  ({done}/{total})", $"Copying {name} …  ({done}/{total})");
        });
        try
        {
            var items = _items.ToList(); // the list may be rescanned meanwhile
            var result = await Task.Run(() => Importer.Import(items, progress));
            ImportedSomething |= result.Copied > 0;
            TxtResult.Text = Txt.L($"Fertig! {result.Copied} importiert", $"Done! {result.Copied} imported")
                             + (result.Skipped > 0 ? Txt.L($", {result.Skipped} übersprungen (schon vorhanden)", $", {result.Skipped} skipped (already there)") : "") + "."
                             + (result.Problems.Count > 0 ? $"\n⚠ {result.Problems.Count} " + Txt.L("Problem(e): ", "problem(s): ") + string.Join("; ", result.Problems.Take(3)) : "");
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, result.Problems.Count > 0 ? "Hub.Danger" : "Hub.Good");
        }
        catch (Exception ex)
        {
            // disk full, drive unplugged … – reported, never a crash; what was copied stays copied
            TxtResult.Text = Txt.L("⚠ Import abgebrochen: ", "⚠ Import stopped: ") + ex.Message;
            TxtResult.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Danger");
        }
        finally
        {
            _busy = false;
            HubAnim.Wobble(Ball, false);
            HubAnim.Click(Ball);
            ImportProgress.Visibility = Visibility.Collapsed;
            HubAnim.SlideIn(TxtResult);
            SourcePanelEnabled(true);
            UpdateButtons();
        }
        await RescanAsync(); // everything imported now shows "schon im MonHub"
    }

    /// <summary>Sources can't change while copying (a rescan would swap the list under the running import).</summary>
    void SourcePanelEnabled(bool enabled) => SourceList.IsEnabled = ItemList.IsEnabled = enabled;

    void Close_Click(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // closing in the middle of copying would leave the player wondering what arrived – finish first
        if (_busy && !App.ShuttingDown)
        {
            e.Cancel = true;
            MessageBox.Show(this, Txt.L("Der Import läuft noch – gleich fertig.", "The import is still running – almost done."), Txt.L("Importieren", "Import"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        base.OnClosing(e);
    }
}
