using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GameBooster.Services;
using GameBooster.ViewModels;

namespace GameBooster;

public partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        Notices.Raised += OnNotice;
        Closed += (_, _) => Notices.Raised -= OnNotice;
        DataContext = new MainViewModel(services);
    }

    private void OnNotice(Notice notice)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnNotice(notice));
            return;
        }
        while (Toasts.Children.Count >= 4)
            Toasts.Children.RemoveAt(0);

        var accent = notice.Kind switch
        {
            NoticeKind.Error => (Brush)FindResource("Brush.Coral"),
            NoticeKind.Success => (Brush)FindResource("Brush.Teal"),
            _ => (Brush)FindResource("Brush.Lime")
        };
        var card = new Border
        {
            Background = (Brush)FindResource("Brush.Card"),
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 420,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = notice.Text,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Brush.Text")
            }
        };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        void CloseToast()
        {
            timer.Stop();
            Toasts.Children.Remove(card);
        }
        card.MouseLeftButtonUp += (_, _) => CloseToast();
        timer.Tick += (_, _) => CloseToast();
        Toasts.Children.Add(card);
        timer.Start();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
