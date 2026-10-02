using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PokeHub;

public partial class AddFangameWindow : Window
{
    bool _nameTouched;

    public string GameName => TxtName.Text.Trim();
    public string GamePath => TxtPath.Text.Trim().Trim('"');

    public AddFangameWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
        Validate();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Txt.L("Start-Datei des Fangames wählen", "Choose the fangame's start file"),
            Filter = Txt.L("Programme", "Programs") + " (*.exe)|*.exe|ROMs (*.nds;*.gba;*.gbc;*.gb)|*.nds;*.gba;*.gbc;*.gb|" + Txt.L("Alle Dateien", "All files") + " (*.*)|*.*",
            InitialDirectory = Directory.Exists(HubPaths.Fangames) ? HubPaths.Fangames : null,
        };
        if (dlg.ShowDialog(this) == true) TxtPath.Text = dlg.FileName;
    }

    void TxtPath_TextChanged(object sender, TextChangedEventArgs e)
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

    void TxtName_TextChanged(object sender, TextChangedEventArgs e)
    {
        _nameTouched = TxtName.IsKeyboardFocused;
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

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
