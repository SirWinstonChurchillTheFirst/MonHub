using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MonHub;

/// <summary>A fangame tile: the program's own icon, and whether its file is still there.</summary>
public class FangameView(Fangame game)
{
    public Fangame Game { get; } = game;
    public ImageSource? Icon { get; } = IconHelper.Get(game.Path);
    public string Status => File.Exists(Game.Path) ? Path.GetFileName(Game.Path) : Txt.L("⚠ Datei nicht gefunden", "⚠ File not found");
}

/// <summary>"Fangames": games made by fans and hacks with their own program – added once, started with one click.</summary>
public partial class FangamesPage : UserControl, IHubPage
{
    public FangamesPage() => InitializeComponent();

    public void Refresh()
    {
        var games = HubConfig.Current.Fangames;
        Tiles.ItemsSource = games.Select(g => new FangameView(g)).ToList();
        EmptyHint.Visibility = games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static Fangame? GameOf(object sender) => ((sender as FrameworkElement)?.DataContext as FangameView)?.Game;

    void Tile_Click(object sender, RoutedEventArgs e)
    {
        if (GameOf(sender) is { } game) Launcher.Start(Window.GetWindow(this), game.Path);
    }

    void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (GameOf(sender) is { } game && Path.GetDirectoryName(game.Path) is { } dir && Directory.Exists(dir)) Shell.OpenFolder(dir);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (GameOf(sender) is not { } game) return;
        if (MessageBox.Show(Window.GetWindow(this), Txt.L($"„{game.Name}“ aus MonHub entfernen?\n(Das Spiel selbst wird nicht gelöscht.)",
                    $"Remove “{game.Name}” from MonHub?\n(The game itself is not deleted.)"), Txt.L("Fangame entfernen", "Remove fangame"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        HubConfig.Current.Fangames.Remove(game);
        HubConfig.Current.Save();
        Refresh();
    }

    void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AddFangameWindow { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        HubConfig.Current.Fangames.Add(new Fangame { Name = dlg.GameName, Path = dlg.GamePath });
        HubConfig.Current.Save();
        Refresh();
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(HubPaths.Fangames);
}
