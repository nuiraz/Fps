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

        var log = new AppLog();
        Faults.Attach(log);
        DispatcherUnhandledException += (_, args) =>
        {
            Faults.Report(args.Exception, "Exception interface");
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Faults.Report(ex, "Exception non gérée", showUi: !args.IsTerminating);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Faults.Report(args.Exception, "Tâche en arrière-plan");
            args.SetObserved();
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
            NoticeWindow.ShowModal("Game Booster est déjà ouvert.", NoticeKind.Info);
            Shutdown();
            return;
        }

        _services = AppServices.Create(Dispatcher, log);
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
