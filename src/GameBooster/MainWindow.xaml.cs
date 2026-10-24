using System.Windows;
using GameBooster.Services;
using GameBooster.ViewModels;

namespace GameBooster;

public partial class MainWindow : Window
{
    public MainWindow(AppServices services)
    {
        InitializeComponent();
        DataContext = new MainViewModel(services);
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
