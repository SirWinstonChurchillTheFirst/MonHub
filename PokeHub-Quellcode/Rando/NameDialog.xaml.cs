using System.IO;
using System.Windows;
using System.Windows.Input;

namespace RandoApp;

public partial class NameDialog : Window
{
    readonly Func<string, bool> _isTaken;

    public string ChosenName { get; private set; }

    public NameDialog(string suggestion, Func<string, bool> isTaken)
    {
        InitializeComponent();
        WindowFit.Apply(this);
        _isTaken = isTaken;
        ChosenName = suggestion;
        TxtName.Text = suggestion;
        Loaded += (_, _) => { TxtName.Focus(); TxtName.SelectAll(); };
    }

    string? Problem(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bitte einen Namen eingeben.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "Enthält Zeichen, die in Dateinamen nicht erlaubt sind (\\ / : * ? \" < > |).";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "Darf nicht mit Punkt oder Leerzeichen enden.";
        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^(CON|PRN|AUX|NUL|COM\d|LPT\d)(\..*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return "Diesen Namen reserviert Windows – bitte einen anderen wählen.";
        if (_isTaken(name)) return "Den Namen gibt es schon (ROM, Spielstand oder Cheat-Datei) – bitte einen anderen wählen.";
        return null;
    }

    void TxtName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var problem = Problem(TxtName.Text.Trim());
        TxtError.Text = problem ?? "";
        BtnOk.IsEnabled = problem == null;
    }

    void TxtName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && BtnOk.IsEnabled) BtnOk_Click(sender, e);
    }

    void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        ChosenName = TxtName.Text.Trim();
        DialogResult = true;
    }

    void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
