using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace MonHub;

/// <summary>
/// "Spielstände ohne Spiel": saves whose ROM is gone (deleted or renamed in the Explorer, imported without ROM).
/// Pick some or all and they go to the recycle bin – all files of a name at once (melonDS, DeSmuME, savestates).
/// </summary>
public partial class OrphanWindow : Window
{
    bool _updating;

    public OrphanWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        Reload();
    }

    void Reload()
    {
        var orphans = GameLibrary.OrphanSaves();
        SaveList.ItemsSource = orphans;
        long size = orphans.Sum(o => o.Size);
        TxtCount.Text = orphans.Count == 0 ? ""
            : Txt.L($"{orphans.Count} {(orphans.Count == 1 ? "Spiel" : "Spiele")} · {orphans.Sum(o => o.Files.Count)} Dateien · {SizeText(size)}",
                    $"{orphans.Count} {(orphans.Count == 1 ? "game" : "games")} · {orphans.Sum(o => o.Files.Count)} files · {SizeText(size)}");
        EmptyPanel.Visibility = orphans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ChkAll.Visibility = orphans.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateButtons();
    }

    static string SizeText(long size) => size >= 1 << 20 ? $"{size / (double)(1 << 20):0.#} MB" : $"{Math.Max(1, size / 1024)} KB";

    void SaveList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    void UpdateButtons()
    {
        int selected = SaveList.SelectedItems.Count, total = SaveList.Items.Count;
        _updating = true;
        ChkAll.IsChecked = total > 0 && selected == total;
        _updating = false;
        TxtDelete.Text = selected == 0 ? Txt.L("Löschen", "Delete") : Txt.L($"{selected} löschen", $"Delete {selected}");
        BtnDelete.IsEnabled = selected > 0;
    }

    void All_Toggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (ChkAll.IsChecked == true) SaveList.SelectAll();
        else SaveList.UnselectAll();
    }

    void Delete_Click(object sender, RoutedEventArgs e)
    {
        var chosen = SaveList.SelectedItems.Cast<OrphanSave>().ToList();
        var title = Txt.L("Spielstände löschen", "Delete saves");
        if (chosen.Count == 0 || Launcher.EmulatorsOpen(this, title)) return;

        var lines = chosen.Take(6).Select(o => "•  " + o.Name).ToList();
        if (chosen.Count > 6) lines.Add(Txt.L($"    … und {chosen.Count - 6} weitere", $"    … and {chosen.Count - 6} more"));
        var list = string.Join("\n", lines);
        var question = chosen.Count == 1
            ? Txt.L($"Wirklich diesen Spielstand löschen?\n\n{list}\n\nEr kommt in den Papierkorb – von dort kannst du ihn zurückholen.",
                    $"Really delete this save?\n\n{list}\n\nIt goes to the Recycle Bin – you can restore it from there.")
            : Txt.L($"Wirklich diese {chosen.Count} Spielstände löschen?\n\n{list}\n\nSie kommen in den Papierkorb – von dort kannst du sie zurückholen.",
                    $"Really delete these {chosen.Count} saves?\n\n{list}\n\nThey go to the Recycle Bin – you can restore them from there.");
        if (MessageBox.Show(this, question, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (!RecycleBin.Delete(chosen.SelectMany(o => o.Files).Select(f => f.Path).ToArray()))
            MessageBox.Show(this, Txt.L("Nicht alles konnte gelöscht werden – ist eine Datei noch irgendwo geöffnet?",
                "Not everything could be deleted – is a file still open somewhere?"), title, MessageBoxButton.OK, MessageBoxImage.Warning);
        Reload();
    }

    void ShowFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not OrphanSave orphan) return;
        var file = orphan.Files.FirstOrDefault(f => File.Exists(f.Path))?.Path;
        if (file != null)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
    }
}
