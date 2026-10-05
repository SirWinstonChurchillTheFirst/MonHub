using Avalonia.Controls.Shapes;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace MonHub;

/// <summary>
/// Every MonHub window: its own title bar in the theme's colours (drag to move, double-click to maximize), and dialogs
/// that answer before the next line runs – <c>if (dialog.ShowDialog() == true) …</c> – on Windows and Linux alike.
/// </summary>
public class HubWindow : Window
{
    public const double TitleBarHeight = 34;

    /// <summary>Open dialogs: while one is up, the windows behind it rest (no refreshing, no animations).</summary>
    public static int OpenDialogs { get; private set; }

    /// <summary>A dialog is open in front of the window.</summary>
    public static bool Blocked(Window? window) => OpenDialogs > 0 && window is not HubWindow { _modal: true };

    public static event Action? BlockedChanged;

    /// <summary>The dialogs that are open right now, the newest last (screenshots and tests look at them).</summary>
    public static List<HubWindow> Dialogs { get; } = [];

    /// <summary>The window this dialog belongs to (it opens centred over it and blocks it).</summary>
    public new Window? Owner { get; set; }

    bool? _result;
    bool _modal;
    DispatcherFrame? _frame;

    /// <summary>Setting it closes the dialog with that answer.</summary>
    public bool? DialogResult
    {
        get => _result;
        set
        {
            _result = value;
            Close();
        }
    }

    protected override Type StyleKeyOverride => typeof(Window);

    public HubWindow()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows' frame stays (snapping, resizing, shadow, rounded corners) – only its title bar is ours
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = TitleBarHeight;
        }
        else
        {
            // Linux: no frame from the window manager (they all look different) – moving and resizing are ours
            SystemDecorations = SystemDecorations.None;
        }
        // a dialog's "cancel" button (IsCancel, also reached with Esc) closes it – after the button's own handler ran
        AddHandler(Button.ClickEvent, (_, e) =>
        {
            if (_modal && e.Source is Button { IsCancel: true })
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsVisible) Close();
                });
        }, handledEventsToo: true);
    }

    /// <summary>Opens the dialog and returns when it is closed: true = confirmed, false or null = not.</summary>
    public bool? ShowDialog()
    {
        var owner = Owner ?? Compat.MainWindow;
        if (owner is { IsVisible: false }) owner = null;
        _modal = true;
        _frame = new DispatcherFrame();
        OpenDialogs++;
        Dialogs.Add(this);
        BlockedChanged?.Invoke();
        Closed += OnDialogClosed;
        try
        {
            if (owner != null && !ReferenceEquals(owner, this)) _ = ShowDialog(owner);
            else Show();
            Dispatcher.UIThread.PushFrame(_frame);
        }
        finally
        {
            Closed -= OnDialogClosed;
            Dialogs.Remove(this);
            if (_frame != null)
            {
                _frame = null;
                OpenDialogs--;
                BlockedChanged?.Invoke();
            }
        }
        return _result;
    }

    void OnDialogClosed(object? sender, EventArgs e)
    {
        if (_frame == null) return;
        _frame.Continue = false;
        _frame = null;
        OpenDialogs--;
        BlockedChanged?.Invoke();
    }

    /// <summary>Waits for something asynchronous (a file picker) while the UI keeps running.</summary>
    public static T Wait<T>(Task<T> task)
    {
        if (task.IsCompleted) return task.GetAwaiter().GetResult();
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false));
        Dispatcher.UIThread.PushFrame(frame);
        return task.GetAwaiter().GetResult();
    }

    // ---------------- the title bar ----------------

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<Control>("PART_TitleBar") is { } bar) HookTitleBar(bar);
        if (e.NameScope.Find<Button>("PART_Close") is { } close) close.Click += (_, _) => Close();
        if (e.NameScope.Find<Panel>("PART_Resize") is { } grips) HookResize(grips);
    }

    /// <summary>Drag to move, double-click to maximize (if the window can be resized).</summary>
    protected void HookTitleBar(Control bar)
    {
        bar.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(bar).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount == 2 && CanResize)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }
            BeginMoveDrag(e);
        };
    }

    /// <summary>Linux: the edges resize (Windows keeps its own frame for that).</summary>
    protected void HookResize(Panel grips)
    {
        if (OperatingSystem.IsWindows())
        {
            grips.IsVisible = false;
            return;
        }
        void Refresh() => grips.IsVisible = CanResize && WindowState == WindowState.Normal;
        Refresh();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty || e.Property == CanResizeProperty) Refresh();
        };
        foreach (var grip in grips.Children.OfType<Border>())
        {
            if (grip.Tag is not string edge || !Enum.TryParse<WindowEdge>(edge, out var windowEdge)) continue;
            grip.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed) BeginResizeDrag(windowEdge, e);
            };
        }
    }
}

/// <summary>A message with buttons in MonHub's look; returns the button that was pressed.</summary>
public static class MessageBox
{
    public static MessageBoxResult Show(string text, string caption = "MonHub", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None) =>
        Show(null, text, caption, buttons, image, defaultResult);

    public static MessageBoxResult Show(Window? owner, string text, string caption = "MonHub", MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None, MessageBoxResult defaultResult = MessageBoxResult.None)
    {
        if (App.ShuttingDown) return MessageBoxResult.None;
        var result = buttons == MessageBoxButton.OK ? MessageBoxResult.OK : buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.Cancel;
        var window = new HubWindow
        {
            Title = caption,
            Owner = owner,
            CanResize = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 340,
            MaxWidth = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        if (window.TryFindResource("Hub.DialogWindow") is ControlTheme theme) window.Theme = theme;
        window.Bind(TemplatedControl.BackgroundProperty, window.GetResourceObservable("Hub.Dialog"));

        var message = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 440 };
        message.Classes.Add("body");
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };

        void Add(string label, MessageBoxResult value, bool primary)
        {
            var button = new Button { Content = label, MinWidth = 86 };
            if (window.TryFindResource(primary ? "Hub.PrimaryKeySmall" : "Hub.Key") is ControlTheme look) button.Theme = look;
            button.IsDefault = value == defaultResult || (defaultResult == MessageBoxResult.None && primary);
            button.IsCancel = value is MessageBoxResult.Cancel or MessageBoxResult.No && buttons != MessageBoxButton.YesNoCancel || value == MessageBoxResult.OK && buttons == MessageBoxButton.OK;
            button.Click += (_, _) =>
            {
                result = value;
                window.Close();
            };
            row.Children.Add(button);
        }

        switch (buttons)
        {
            case MessageBoxButton.OK:
                Add("OK", MessageBoxResult.OK, true);
                break;
            case MessageBoxButton.OKCancel:
                Add("OK", MessageBoxResult.OK, true);
                Add(Txt.L("Abbrechen", "Cancel"), MessageBoxResult.Cancel, false);
                break;
            case MessageBoxButton.YesNo:
                Add(Txt.L("Ja", "Yes"), MessageBoxResult.Yes, true);
                Add(Txt.L("Nein", "No"), MessageBoxResult.No, false);
                break;
            case MessageBoxButton.YesNoCancel:
                Add(Txt.L("Ja", "Yes"), MessageBoxResult.Yes, true);
                Add(Txt.L("Nein", "No"), MessageBoxResult.No, false);
                Add(Txt.L("Abbrechen", "Cancel"), MessageBoxResult.Cancel, false);
                break;
        }

        var body = new DockPanel();
        if (Mark(image) is { } mark)
        {
            DockPanel.SetDock(mark, Dock.Left);
            body.Children.Add(mark);
        }
        body.Children.Add(message);
        var content = new StackPanel { Margin = new Thickness(22, 14, 22, 18) };
        content.Children.Add(body);
        content.Children.Add(row);
        window.Content = content;
        window.ShowDialog();
        return result;
    }

    /// <summary>A round mark in front of the text: ! (warning), × (error), ? (question), i (information).</summary>
    static Control? Mark(MessageBoxImage image)
    {
        var (glyph, colour) = image switch
        {
            MessageBoxImage.Warning => ("!", "#E0A21B"),
            MessageBoxImage.Error => ("×", "#D9412E"),
            MessageBoxImage.Question => ("?", "#3D8BD9"),
            MessageBoxImage.Information => ("i", "#3D8BD9"),
            _ => ("", ""),
        };
        if (glyph.Length == 0) return null;
        return new Grid
        {
            Width = 34,
            Height = 34,
            Margin = new Thickness(0, 0, 16, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Children =
            {
                new Ellipse { Fill = Avalonia.Media.Brush.Parse(colour) },
                new TextBlock { Text = glyph, FontSize = 20, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            },
        };
    }
}

/// <summary>File and folder pickers that return when the player has chosen (null = cancelled).</summary>
public static class Pick
{
    static IStorageProvider? Storage(Window? owner) => (owner ?? Compat.MainWindow)?.StorageProvider;

    static FilePickerFileType[] Types((string Name, string[] Patterns)[]? filters) =>
        filters?.Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns }).ToArray() ?? [];

    static IStorageFolder? Folder(IStorageProvider storage, string? dir)
    {
        if (dir == null || !Directory.Exists(dir)) return null;
        try { return HubWindow.Wait(storage.TryGetFolderFromPathAsync(dir)); }
        catch { return null; }
    }

    public static string[] OpenFiles(Window? owner, string title, (string Name, string[] Patterns)[]? filters = null, bool multiple = false, string? startIn = null)
    {
        if (Storage(owner) is not { } storage) return [];
        var files = HubWindow.Wait(storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title, AllowMultiple = multiple, FileTypeFilter = Types(filters), SuggestedStartLocation = Folder(storage, startIn),
        }));
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }

    public static string? OpenFile(Window? owner, string title, (string Name, string[] Patterns)[]? filters = null, string? startIn = null) =>
        OpenFiles(owner, title, filters, false, startIn).FirstOrDefault();

    public static string? SaveFile(Window? owner, string title, string suggestedName, (string Name, string[] Patterns)[]? filters = null, string? startIn = null, string? extension = null)
    {
        if (Storage(owner) is not { } storage) return null;
        var file = HubWindow.Wait(storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title, SuggestedFileName = suggestedName, FileTypeChoices = Types(filters), DefaultExtension = extension, SuggestedStartLocation = Folder(storage, startIn),
        }));
        return file?.TryGetLocalPath();
    }

    public static string? Folder(Window? owner, string title, string? startIn = null)
    {
        if (Storage(owner) is not { } storage) return null;
        var folders = HubWindow.Wait(storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, SuggestedStartLocation = Folder(storage, startIn) }));
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
