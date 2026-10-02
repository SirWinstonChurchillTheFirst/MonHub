using System.Windows;

namespace PokeHub;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
    }
}
