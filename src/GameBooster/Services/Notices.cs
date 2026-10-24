using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GameBooster.Services;

public enum NoticeKind
{
    Info,
    Success,
    Error
}

public sealed class Notice
{
    public Notice(string text, NoticeKind kind)
    {
        Text = text;
        Kind = kind;
    }

    public string Text { get; }
    public NoticeKind Kind { get; }
}

public static class Notices
{
    private static string? _lastText;
    private static DateTime _lastAt;

    public static event Action<Notice>? Raised;

    public static bool HasListener => Raised != null;

    public static void Show(string text, NoticeKind kind = NoticeKind.Info)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        var now = DateTime.UtcNow;
        if (text == _lastText && (now - _lastAt).TotalSeconds < 4)
            return;
        _lastText = text;
        _lastAt = now;

        var app = Application.Current;
        if (app == null)
            return;
        if (app.Dispatcher.CheckAccess())
            Deliver(text, kind);
        else
            app.Dispatcher.BeginInvoke(() => Deliver(text, kind));
    }

    private static void Deliver(string text, NoticeKind kind)
    {
        if (Raised != null)
            Raised.Invoke(new Notice(text, kind));
        else
            NoticeWindow.ShowModal(text, kind);
    }
}

public static class Faults
{
    private static AppLog? _log;

    public static void Attach(AppLog log) => _log = log;

    public static void Report(Exception ex, string context, bool showUi = true)
    {
        try { _log?.Error(context, ex); } catch { /* journal indisponible */ }
        if (!showUi)
            return;
        Notices.Show(Friendly(ex), NoticeKind.Error);
    }

    public static void Note(string message)
    {
        try { _log?.Error(message); } catch { /* journal indisponible */ }
    }

    public static string Friendly(Exception ex)
    {
        if (ex is InvalidOperationException invalid &&
            invalid.Message.Contains("liaison", StringComparison.OrdinalIgnoreCase))
            return "L'affichage a rencontré une erreur. Le détail est dans le journal : %AppData%\\GameBooster\\logs\\app.log.";
        if (ex is ApiException or HttpRequestException or TaskCanceledException)
            return ErrorText.French(ex);
        var french = ErrorText.French(ex);
        if (!string.Equals(french, "Une erreur est survenue.", StringComparison.Ordinal))
            return french;
        return "Une erreur est survenue. Le détail est dans le journal : %AppData%\\GameBooster\\logs\\app.log.";
    }
}

public static class Dialogs
{
    public static bool Confirm(string text)
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            return app.Dispatcher.Invoke(() => Confirm(text));
        var dialog = new NoticeWindow(text, NoticeKind.Info, confirm: true);
        return dialog.ShowDialog() == true && dialog.Accepted;
    }

    public static void Info(string text) => Notices.Show(text, NoticeKind.Info);

    public static void Error(string text)
    {
        Faults.Note(text);
        Notices.Show(text, NoticeKind.Error);
    }
}

public sealed class NoticeWindow : Window
{
    public bool Accepted { get; private set; }

    public NoticeWindow(string text, NoticeKind kind, bool confirm = false)
    {
        Title = "Game Booster";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = Application.Current?.MainWindow == null;
        Background = BrushOf("Brush.Bg");
        Foreground = BrushOf("Brush.Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 14;

        var owner = Application.Current?.MainWindow;
        if (owner != null && owner.IsVisible && owner != this)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        var message = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = BrushOf("Brush.Text"),
            Margin = new Thickness(0, 8, 0, 18)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        if (confirm)
        {
            var yes = MakeButton("Oui", primary: true);
            yes.IsDefault = true;
            yes.Margin = new Thickness(0, 0, 8, 0);
            yes.Click += (_, _) =>
            {
                Accepted = true;
                DialogResult = true;
            };
            var no = MakeButton("Non", primary: false);
            no.IsCancel = true;
            no.Click += (_, _) => DialogResult = false;
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
        }
        else
        {
            var ok = MakeButton("OK", primary: true);
            ok.IsDefault = true;
            ok.IsCancel = true;
            ok.Click += (_, _) => DialogResult = true;
            buttons.Children.Add(ok);
        }

        var accent = kind == NoticeKind.Error ? BrushOf("Brush.Coral") : BrushOf("Brush.Lime");
        Content = new Border
        {
            Background = BrushOf("Brush.Card"),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 16, 20, 16),
            Margin = new Thickness(12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "Game Booster",
                        FontWeight = FontWeights.SemiBold,
                        Foreground = accent
                    },
                    message,
                    buttons
                }
            }
        };
    }

    public static void ShowModal(string text, NoticeKind kind)
    {
        var window = new NoticeWindow(text, kind);
        window.ShowDialog();
    }

    private static Button MakeButton(string caption, bool primary)
    {
        var button = new Button { Content = caption, MinWidth = 88 };
        if (Application.Current?.TryFindResource(primary ? "PrimaryButton" : typeof(Button)) is Style style)
            button.Style = style;
        return button;
    }

    private static Brush BrushOf(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;
        return key.Contains("Bg", StringComparison.Ordinal) ? Brushes.Black : Brushes.White;
    }
}
