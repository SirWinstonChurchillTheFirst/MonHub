
namespace MonHub;

public partial class HelpWindow : HubWindow
{
    public HelpWindow()
    {
        InitializeComponent();
        RandoApp.WindowFit.Apply(this);
    }

    void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
