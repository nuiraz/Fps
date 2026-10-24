using System.Windows.Threading;
using GameBooster.Models;

namespace GameBooster.Services;

public sealed class AppState
{
    private readonly Dispatcher _ui;

    public AppState(Dispatcher ui) => _ui = ui;

    public object Gate { get; } = new();
    public MachineSettings Machine { get; set; } = new();
    public MachineBackup Backup { get; set; } = new();
    public SessionData? Session { get; set; }
    public ProfileCache Profile { get; set; } = new();
    public string SyncStatus { get; set; } = "Non connecté";
    public string? LastSyncError { get; set; }
    public bool BoostApplied { get; set; }

    public event Action? Changed;

    public string ProfileKey
    {
        get
        {
            var name = Session?.Username;
            return string.IsNullOrWhiteSpace(name) ? "_local" : name.Trim().ToLowerInvariant();
        }
    }

    public bool IsLoggedIn => Session != null;

    public void Notify()
    {
        void Raise() => Changed?.Invoke();
        try
        {
            if (_ui.CheckAccess())
                Raise();
            else
                _ui.BeginInvoke(Raise);
        }
        catch (Exception)
        {
            // Fermeture de la fenêtre.
        }
    }
}
