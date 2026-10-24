using GameBooster.Models;

namespace GameBooster.Services;

public sealed class SyncService : IDisposable
{
    private readonly AppState _state;
    private readonly LocalStore _store;
    private readonly ApiClient _api;
    private readonly AppLog _log;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _debounce;
    private int _edits;

    public SyncService(AppState state, LocalStore store, ApiClient api, AppLog log)
    {
        _state = state;
        _store = store;
        _api = api;
        _log = log;
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _timer.Tick += async (_, _) =>
        {
            if (_state.IsLoggedIn)
                await SyncNowAsync();
        };
        _timer.Start();
    }

    public void Schedule()
    {
        if (!_state.IsLoggedIn)
            return;
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        var token = cts.Token;
        _ = Debounced(token);
    }

    public async Task<string?> LoginAsync(string username, string password, bool register, bool remember)
    {
        var nameError = Guard.ValidateUsername(username);
        if (nameError != null)
            return nameError;
        var passwordError = Guard.ValidatePassword(password);
        if (passwordError != null)
            return passwordError;
        if (Guard.IsPlaceholder(_state.Machine.ApiBaseUrl))
            return "Indiquez l'URL réelle du Worker dans Paramètres. L'adresse contient encore <subdomain>.";

        SessionData session;
        try
        {
            session = register
                ? await _api.RegisterAsync(username, password, remember)
                : await _api.LoginAsync(username, password, remember);
        }
        catch (Exception ex)
        {
            _log.Error(register ? "Inscription refusée" : "Connexion refusée", ex);
            return ErrorText.French(ex);
        }

        _state.Machine.RememberSession = remember;
        _store.SaveMachine(_state.Machine);
        _state.Session = session;
        if (remember)
            _store.SaveSession(session);
        else
            _store.ClearSession();
        _state.Profile = _store.LoadProfile(session.Username);
        _state.SyncStatus = "Synchronisation…";
        _state.Notify();
        await SyncNowAsync();
        return null;
    }

    public async Task LogoutAsync()
    {
        try
        {
            if (_state.IsLoggedIn)
                await _api.LogoutAsync();
        }
        catch (Exception ex)
        {
            _log.Error("Déconnexion distante impossible", ex);
        }
        _state.Session = null;
        _store.ClearSession();
        _state.Profile = _store.LoadProfile("_local");
        _state.SyncStatus = "Non connecté";
        _state.LastSyncError = null;
        _state.Notify();
    }

    public async Task VerifyRememberedSessionAsync()
    {
        if (!_state.IsLoggedIn)
        {
            _state.SyncStatus = "Non connecté";
            _state.Notify();
            return;
        }
        if (Guard.IsPlaceholder(_state.Machine.ApiBaseUrl))
        {
            _state.SyncStatus = "URL de l'API à configurer";
            _state.Notify();
            return;
        }
        try
        {
            await _api.GetSettingsAsync();
        }
        catch (ApiException ex) when (ex.Status == 401)
        {
            _state.Session = null;
            _store.ClearSession();
            _state.Profile = _store.LoadProfile("_local");
            _state.SyncStatus = "Session expirée";
            _state.Notify();
            return;
        }
        catch (Exception ex)
        {
            _state.SyncStatus = "Hors ligne — cache local conservé";
            _state.LastSyncError = ErrorText.French(ex);
            _state.Notify();
            return;
        }
        await SyncNowAsync();
    }

    public async Task SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!_state.IsLoggedIn)
        {
            _state.SyncStatus = "Non connecté";
            _state.Notify();
            return;
        }
        var entered = false;
        var editsAtStart = Volatile.Read(ref _edits);
        try
        {
            await _gate.WaitAsync(cancellationToken);
            entered = true;
            _state.SyncStatus = "Synchronisation…";
            _state.Notify();
            await SyncSettings(cancellationToken);
            await SyncPresets(cancellationToken);
            await SyncHistory(cancellationToken);
            lock (_state.Gate)
            {
                _state.Profile.LastSyncAt = TimeStamp.Now();
                _store.SaveProfile(_state.ProfileKey, _state.Profile);
            }
            _state.LastSyncError = null;
            _state.SyncStatus = "Synchronisé " + TimeStamp.Local(_state.Profile.LastSyncAt);
            _state.Notify();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // une synchro plus récente a pris le relais
        }
        catch (ApiException ex) when (ex.Status == 401)
        {
            _state.SyncStatus = "Session expirée";
            _state.LastSyncError = ErrorText.French(ex);
            _state.Notify();
        }
        catch (Exception ex)
        {
            _log.Error("Synchronisation interrompue", ex);
            _state.SyncStatus = "Hors ligne — modifications conservées sur cet ordinateur";
            _state.LastSyncError = ErrorText.French(ex);
            _state.Notify();
        }
        finally
        {
            if (entered)
                _gate.Release();
            if (Volatile.Read(ref _edits) != editsAtStart)
                Schedule();
        }
    }

    public void SaveProfile()
    {
        Interlocked.Increment(ref _edits);
        lock (_state.Gate)
            _store.SaveProfile(_state.ProfileKey, _state.Profile);
        _state.Notify();
        Schedule();
    }

    private async Task Debounced(CancellationToken token)
    {
        try
        {
            await Task.Delay(700, token);
            await SyncNowAsync(token);
        }
        catch (OperationCanceledException)
        {
            // remplacé
        }
    }

    private async Task SyncSettings(CancellationToken cancellationToken)
    {
        var remote = await _api.GetSettingsAsync(cancellationToken);
        if (!remote.Ok || remote.Value == null)
            throw new ApiException(remote.Status, "Réglages distants illisibles.", remote.Raw);
        SettingsDocument local;
        var upload = false;
        lock (_state.Gate)
        {
            var cleaned = PreferenceRules.Sanitize(_state.Profile.Settings.Preferences);
            var dirty = PreferenceRules.Changed(_state.Profile.Settings.Preferences, cleaned);
            _state.Profile.Settings.Preferences = cleaned;
            if (!TimeStamp.TryParse(_state.Profile.Settings.UpdatedAt, out _))
            {
                _state.Profile.Settings.UpdatedAt = TimeStamp.Touch(null);
                dirty = true;
            }
            var compare = TimeStamp.Compare(_state.Profile.Settings.UpdatedAt, remote.Value.UpdatedAt);
            if (compare < 0)
            {
                _state.Profile.Settings = remote.Value;
                return;
            }
            if (compare == 0 && dirty)
            {
                _state.Profile.Settings.UpdatedAt = TimeStamp.Touch(_state.Profile.Settings.UpdatedAt);
                compare = 1;
            }
            upload = compare > 0;
            local = _state.Profile.Settings;
        }
        if (!upload)
            return;
        var put = await _api.PutSettingsAsync(local, cancellationToken);
        lock (_state.Gate)
            _state.Profile.Settings = put.Value ?? local;
    }

    private async Task SyncPresets(CancellationToken cancellationToken)
    {
        var remoteResult = await _api.GetPresetsAsync(includeDeleted: true, cancellationToken);
        if (!remoteResult.Ok || remoteResult.Value == null)
            throw new ApiException(remoteResult.Status, "Presets distants illisibles.", remoteResult.Raw);
        var remote = remoteResult.Value.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        List<Preset> local;
        lock (_state.Gate)
            local = _state.Profile.Presets.Where(p => !p.BuiltIn).Select(p => p.Clone()).ToList();

        foreach (var preset in local)
        {
            var originalId = preset.Id;
            if (!PreferenceRules.IsPresetId(preset.Id))
            {
                if (preset.Deleted)
                {
                    RemovePreset(originalId);
                    continue;
                }
                preset.Id = Guid.NewGuid().ToString("D");
                preset.UpdatedAt = TimeStamp.Touch(preset.UpdatedAt);
            }
            if (!TimeStamp.TryParse(preset.CreatedAt, out _))
                preset.CreatedAt = TimeStamp.TryParse(preset.UpdatedAt, out _) ? preset.UpdatedAt : TimeStamp.Touch(null);
            if (!TimeStamp.TryParse(preset.UpdatedAt, out _))
                preset.UpdatedAt = TimeStamp.Touch(preset.CreatedAt);
            preset.CreatedAt = TimeStamp.EnsureIso(preset.CreatedAt) ?? preset.CreatedAt;
            preset.UpdatedAt = TimeStamp.EnsureIso(preset.UpdatedAt) ?? preset.UpdatedAt;

            if (!remote.TryGetValue(preset.Id, out var server))
            {
                if (preset.Deleted)
                {
                    RemovePreset(originalId);
                    continue;
                }
                var saved = await _api.UpsertPresetAsync(preset, cancellationToken);
                if (!string.Equals(originalId, preset.Id, StringComparison.OrdinalIgnoreCase))
                    RemovePreset(originalId);
                ReplacePreset(saved.Value ?? preset);
                continue;
            }
            remote.Remove(preset.Id);
            var compare = TimeStamp.Compare(preset.UpdatedAt, server.UpdatedAt);
            if (compare > 0)
            {
                var saved = preset.Deleted
                    ? await _api.DeletePresetAsync(preset.Id, preset.UpdatedAt, cancellationToken)
                    : await _api.UpsertPresetAsync(preset, cancellationToken);
                ReplacePreset(saved.Value ?? server);
            }
            else if (compare < 0)
            {
                ReplacePreset(server);
            }
        }
        foreach (var leftover in remote.Values)
            ReplacePreset(leftover);
    }

    private async Task SyncHistory(CancellationToken cancellationToken)
    {
        List<HistoryEntry> pending;
        lock (_state.Gate)
            pending = _state.Profile.History.Where(h => !h.Synced).ToList();
        foreach (var chunk in pending.Chunk(40))
        {
            await _api.PostHistoryAsync(chunk, cancellationToken);
            lock (_state.Gate)
            {
                foreach (var entry in chunk)
                {
                    var local = _state.Profile.History.FirstOrDefault(h => h.Id == entry.Id);
                    if (local != null)
                        local.Synced = true;
                }
            }
        }
        var remote = await _api.GetHistoryAsync(cancellationToken);
        if (!remote.Ok || remote.Value == null)
            return;
        lock (_state.Gate)
        {
            var known = _state.Profile.History.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in remote.Value)
            {
                entry.Synced = true;
                if (!known.Contains(entry.Id))
                    _state.Profile.History.Add(entry);
            }
            _state.Profile.History = _state.Profile.History
                .OrderByDescending(h => h.OccurredAt, StringComparer.Ordinal)
                .Take(200)
                .ToList();
        }
    }

    private void ReplacePreset(Preset preset)
    {
        preset.BuiltIn = false;
        lock (_state.Gate)
        {
            var index = _state.Profile.Presets.FindIndex(p => p.Id.Equals(preset.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                _state.Profile.Presets[index] = preset;
            else
                _state.Profile.Presets.Add(preset);
        }
    }

    private void RemovePreset(string id)
    {
        lock (_state.Gate)
            _state.Profile.Presets.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _timer.Stop();
        _debounce?.Cancel();
        _gate.Dispose();
    }
}
