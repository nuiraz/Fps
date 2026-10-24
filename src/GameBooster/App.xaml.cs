using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using GameBooster.Services;

namespace GameBooster;

public partial class App : Application
{
    private Mutex? _mutex;
    private bool _mutexOwned;
    private AppServices? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var culture = new CultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("fr-FR")));

        DispatcherUnhandledException += (_, args) =>
        {
            try { _services?.Log.Error("Exception interface", args.Exception); } catch { /* journal indisponible */ }
            MessageBox.Show(args.Exception.Message, "Game Booster", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        try
        {
            _mutex = new Mutex(true, @"Local\GameBooster.SingleInstance", out _mutexOwned);
        }
        catch (AbandonedMutexException)
        {
            _mutexOwned = true;
        }
        if (!_mutexOwned)
        {
            MessageBox.Show("Game Booster est déjà ouvert.", "Game Booster", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _services = AppServices.Create(Dispatcher);
        var window = new MainWindow(_services);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        if (_mutexOwned)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* déjà relâché */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
