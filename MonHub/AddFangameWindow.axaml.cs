using System.IO;

namespace MonHub;

public partial class AddFangameWindow : HubWindow
{
    bool _nameTouched;

    public string GameName => (TxtName.Text ?? "").Trim();
    public string GamePath => (TxtPath.Text ?? "").Trim().Trim('"');

    public AddFangameWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        Validate();
    }

    void Browse_Click(object? sender, RoutedEventArgs e)
    {
        // a Linux program has no ".exe" – there the list starts with "all files"
        (string, string[]) programs = (Txt.L("Programme", "Programs") + " (*.exe)", ["*.exe"]);
        (string, string[]) roms = ("ROMs (*.nds, *.gba, *.gbc, *.gb)", ["*.nds", "*.gba", "*.gbc", "*.gb"]);
        (string, string[]) all = (Txt.L("Alle Dateien", "All files"), ["*"]);
        var file = Pick.OpenFile(this, Txt.L("Start-Datei des Fangames wählen", "Choose the fangame's start file"),
            OperatingSystem.IsWindows() ? [programs, roms, all] : [all, roms], HubPaths.Fangames);
        if (file != null) TxtPath.Text = file;
    }

    void TxtPath_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ImgIcon.Source = IconHelper.Get(GamePath);
        if (!_nameTouched && File.Exists(GamePath))
        {
            // "Game.exe" says nothing – the folder name usually is the game's name
            var file = Path.GetFileNameWithoutExtension(GamePath);
            var folder = Path.GetFileName(Path.GetDirectoryName(GamePath)) ?? file;
            TxtName.Text = file.Equals("game", StringComparison.OrdinalIgnoreCase) || file.Length <= 3 ? folder : file;
            _nameTouched = false;
        }
        Validate();
    }

    void TxtName_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _nameTouched = TxtName.IsFocused;
        TxtPreviewName.Text = GameName.Length > 0 ? GameName : Txt.L("Vorschau", "Preview");
        Validate();
    }

    void Validate()
    {
        if (BtnOk == null) return;
        string? problem = GamePath.Length == 0 ? "" : !File.Exists(GamePath) ? Txt.L("Diese Datei gibt es nicht.", "This file doesn't exist.")
            : GameName.Length == 0 ? Txt.L("Bitte einen Namen eingeben.", "Please enter a name.") : null;
        TxtError.Text = problem ?? "";
        BtnOk.IsEnabled = problem == null;
    }

    void Ok_Click(object? sender, RoutedEventArgs e) => DialogResult = true;
}
