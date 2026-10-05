namespace MonHub;

static class Program
{
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont() // the same body font on every Linux desktop (Windows keeps Segoe UI)
        .LogToTrace();
}
