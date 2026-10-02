using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PokeHub;

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
            : $"{orphans.Count} {(orphans.Count == 1 ? "Spiel" : "Spiele")} · {orphans.Sum(o => o.Files.Count)} Dateien · {SizeText(size)}";
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
        TxtDelete.Text = selected == 0 ? "Löschen" : $"{selected} löschen";
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
        if (chosen.Count == 0 || Launcher.EmulatorsOpen(this, "Spielstände löschen")) return;

        var lines = chosen.Take(6).Select(o => "•  " + o.Name).ToList();
        if (chosen.Count > 6) lines.Add($"    … und {chosen.Count - 6} weitere");
        var what = chosen.Count == 1 ? "diesen Spielstand" : $"diese {chosen.Count} Spielstände";
        if (MessageBox.Show(this, $"Wirklich {what} löschen?\n\n{string.Join("\n", lines)}\n\nSie kommen in den Papierkorb – von dort kannst du sie zurückholen.",
                "Spielstände löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        if (!RecycleBin.Delete(chosen.SelectMany(o => o.Files).Select(f => f.Path).ToArray()))
            MessageBox.Show(this, "Nicht alles konnte gelöscht werden – ist eine Datei noch irgendwo geöffnet?", "Spielstände löschen",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
