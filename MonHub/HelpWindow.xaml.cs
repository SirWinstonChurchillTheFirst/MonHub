using System.Windows;

namespace MonHub;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
    }
}
