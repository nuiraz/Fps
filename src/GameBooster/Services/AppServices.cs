using System.Security.Principal;
using System.Windows.Threading;

namespace GameBooster.Services;

public sealed class AppServices : IDisposable
{
    public AppLog Log { get; }
    public LocalStore Store { get; }
    public AppState State { get; }
    public ApiClient Api { get; }
    public SyncService Sync { get; }
    public PowerPlanService Power { get; }
    public CleanupService Cleanup { get; }
    public ProcessService Processes { get; }
    public NetworkService Network { get; }
    public StartupService Startup { get; }
    public RestorePointService RestorePoints { get; }
    public MetricsService Metrics { get; }
    public GamePriorityService Priority { get; }
    public TweakEngine Tweaks { get; }
    public bool IsAdmin { get; }

    private AppServices(Dispatcher dispatcher, AppLog log)
    {
        Log = log;
        Store = new LocalStore(Log);
        State = new AppState(dispatcher);
        State.Machine = Store.LoadMachine();
        State.Backup = Store.LoadBackup();
        if (State.Machine.RememberSession)
            State.Session = Store.LoadSession();
        else
            Store.ClearSession();
        State.Profile = Store.LoadProfile(State.ProfileKey);
        Api = new ApiClient(State);
        Sync = new SyncService(State, Store, Api, Log);
        Power = new PowerPlanService();
        Cleanup = new CleanupService();
        Processes = new ProcessService();
        Network = new NetworkService();
        Startup = new StartupService();
        RestorePoints = new RestorePointService();
        Metrics = new MetricsService();
        Priority = new GamePriorityService(State, Log);
        Tweaks = new TweakEngine(State, Store, Power, Network, Cleanup, Processes, RestorePoints, Priority, Log)
        {
            RequestSync = () => Sync.Schedule()
        };
        using var identity = WindowsIdentity.GetCurrent();
        IsAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static AppServices Create(Dispatcher dispatcher, AppLog? log = null) => new(dispatcher, log ?? new AppLog());

    public void Dispose()
    {
        Sync.Dispose();
        Priority.Dispose();
        Metrics.Dispose();
        Api.Dispose();
    }
}
