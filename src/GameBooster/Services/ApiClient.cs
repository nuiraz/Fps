using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GameBooster.Models;

namespace GameBooster.Services;

public sealed class ApiResult<T>
{
    public int Status { get; init; }
    public T? Value { get; init; }
    public string Raw { get; init; } = "";
    public bool Ok => Status >= 200 && Status < 300;
    public bool Conflict => Status == 409;
}

public sealed class ApiClient : IDisposable
{
    private readonly AppState _state;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public ApiClient(AppState state)
    {
        _state = state;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("GameBooster/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync<JsonElement>(HttpMethod.Get, "/api/health", null, auth: false, cancellationToken);
        return result.Ok;
    }

    public async Task<SessionData> RegisterAsync(string username, string password, bool remember, CancellationToken cancellationToken = default)
        => await AuthAsync("/api/register", username, password, remember, cancellationToken);

    public async Task<SessionData> LoginAsync(string username, string password, bool remember, CancellationToken cancellationToken = default)
        => await AuthAsync("/api/login", username, password, remember, cancellationToken);

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
        => await SendAsync<JsonElement>(HttpMethod.Post, "/api/logout", new { }, auth: true, cancellationToken);

    public async Task<ApiResult<List<Preset>>> GetPresetsAsync(bool includeDeleted, CancellationToken cancellationToken = default)
    {
        var path = includeDeleted ? "/api/presets?includeDeleted=1" : "/api/presets";
        var result = await SendAsync<List<PresetDto>>(HttpMethod.Get, path, null, true, cancellationToken);
        return new ApiResult<List<Preset>>
        {
            Status = result.Status,
            Raw = result.Raw,
            Value = result.Value?.Select(ToPreset).ToList()
        };
    }

    public async Task<ApiResult<Preset>> UpsertPresetAsync(Preset preset, CancellationToken cancellationToken = default)
    {
        var body = ToDto(preset);
        var result = await SendAsync<PresetDto>(HttpMethod.Put, "/api/presets/" + Uri.EscapeDataString(preset.Id), body, true, cancellationToken);
        if (result.Conflict)
        {
            var conflict = ParseEnvelopePreset(result.Raw);
            return new ApiResult<Preset> { Status = 409, Raw = result.Raw, Value = conflict };
        }
        return new ApiResult<Preset> { Status = result.Status, Raw = result.Raw, Value = result.Value == null ? null : ToPreset(result.Value) };
    }

    public async Task<ApiResult<Preset>> DeletePresetAsync(string id, string updatedAt, CancellationToken cancellationToken = default)
    {
        var stamp = TimeStamp.EnsureIso(updatedAt) ?? TimeStamp.Touch(updatedAt);
        var path = "/api/presets/" + Uri.EscapeDataString(id) + "?updatedAt=" + Uri.EscapeDataString(stamp);
        var result = await SendAsync<PresetDto>(HttpMethod.Delete, path, new { updatedAt = stamp }, true, cancellationToken);
        if (result.Conflict)
            return new ApiResult<Preset> { Status = 409, Raw = result.Raw, Value = ParseEnvelopePreset(result.Raw) };
        return new ApiResult<Preset> { Status = result.Status, Raw = result.Raw, Value = result.Value == null ? null : ToPreset(result.Value) };
    }

    public Task<ApiResult<SettingsDocument>> GetSettingsAsync(CancellationToken cancellationToken = default)
        => SendAsync<SettingsDocument>(HttpMethod.Get, "/api/settings", null, true, cancellationToken);

    public async Task<ApiResult<SettingsDocument>> PutSettingsAsync(SettingsDocument settings, CancellationToken cancellationToken = default)
    {
        var payload = new SettingsDocument
        {
            UpdatedAt = TimeStamp.EnsureIso(settings.UpdatedAt) ?? TimeStamp.Touch(settings.UpdatedAt),
            Preferences = PreferenceRules.Sanitize(settings.Preferences)
        };
        var result = await SendAsync<SettingsDocument>(HttpMethod.Put, "/api/settings", payload, true, cancellationToken);
        if (!result.Conflict)
            return result;
        SettingsDocument? current = null;
        try
        {
            using var doc = JsonDocument.Parse(result.Raw);
            if (doc.RootElement.TryGetProperty("settings", out var node))
                current = node.Deserialize<SettingsDocument>(AppJson.Options);
        }
        catch
        {
            current = null;
        }
        return new ApiResult<SettingsDocument> { Status = 409, Raw = result.Raw, Value = current };
    }

    public async Task PostHistoryAsync(IReadOnlyList<HistoryEntry> entries, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            entries = entries.Select(e => new
            {
                id = e.Id,
                action = e.Action,
                detail = e.Detail,
                success = e.Success,
                occurredAt = e.OccurredAt
            }).ToList()
        };
        var result = await SendAsync<JsonElement>(HttpMethod.Post, "/api/history", payload, true, cancellationToken);
        if (!result.Ok)
            throw new ApiException(result.Status, ReadError(result.Raw, result.Status), result.Raw);
    }

    public Task<ApiResult<List<HistoryEntry>>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => SendAsync<List<HistoryEntry>>(HttpMethod.Get, "/api/history?limit=200", null, true, cancellationToken);

    private async Task<SessionData> AuthAsync(string path, string username, string password, bool remember, CancellationToken cancellationToken)
    {
        var result = await SendAsync<AuthResponse>(HttpMethod.Post, path, new { username, password, remember }, false, cancellationToken);
        if (!result.Ok || result.Value == null || string.IsNullOrWhiteSpace(result.Value.Token))
            throw new ApiException(result.Status, ReadError(result.Raw, result.Status), result.Raw);
        return new SessionData
        {
            Token = result.Value.Token,
            UserId = result.Value.User?.Id ?? "",
            Username = result.Value.User?.Username ?? username.Trim().ToLowerInvariant(),
            ExpiresAt = result.Value.ExpiresAt ?? ""
        };
    }

    private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body, bool auth, CancellationToken cancellationToken)
    {
        var baseUrl = Guard.NormalizeApiBase(_state.Machine.ApiBaseUrl);
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var root))
            throw new ApiException(0, "L'URL de l'API est invalide.", "");
        var request = new HttpRequestMessage(method, new Uri(root, path.TrimStart('/')))
        {
            Content = body == null ? null : new StringContent(JsonSerializer.Serialize(body, AppJson.Compact), Encoding.UTF8, "application/json")
        };
        if (auth)
        {
            var token = _state.Session?.Token;
            if (string.IsNullOrWhiteSpace(token))
                throw new ApiException(401, "Session expirée. Reconnectez-vous.", "");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiException(0, "Impossible de joindre le serveur. Vérifiez l'URL de l'API et la connexion.", "");
        }
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode == 409)
            return new ApiResult<T> { Status = 409, Raw = raw };
        if (!response.IsSuccessStatusCode)
            throw new ApiException((int)response.StatusCode, ReadError(raw, (int)response.StatusCode), raw);
        T? value = default;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try { value = JsonSerializer.Deserialize<T>(raw, AppJson.Options); }
            catch (Exception ex)
            {
                throw new ApiException((int)response.StatusCode, "Réponse du serveur illisible.", raw + " " + ex.Message);
            }
        }
        return new ApiResult<T> { Status = (int)response.StatusCode, Raw = raw, Value = value };
    }

    private static string ReadError(string raw, int status)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var text = error.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }
        catch
        {
            // corps non JSON
        }
        return status == 0 ? "Impossible de joindre le serveur." : "Le serveur a répondu " + status + ".";
    }

    private static Preset? ParseEnvelopePreset(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("preset", out var node))
                return null;
            var dto = node.Deserialize<PresetDto>(AppJson.Options);
            return dto == null ? null : ToPreset(dto);
        }
        catch
        {
            return null;
        }
    }

    private static PresetDto ToDto(Preset preset)
    {
        var updated = TimeStamp.EnsureIso(preset.UpdatedAt) ?? TimeStamp.Touch(preset.UpdatedAt);
        var created = TimeStamp.EnsureIso(preset.CreatedAt) ?? updated;
        return new PresetDto
        {
            Id = preset.Id,
            Name = preset.Name,
            Description = preset.Description,
            Tweaks = preset.Tweaks,
            CreatedAt = created,
            UpdatedAt = updated,
            Deleted = preset.Deleted
        };
    }

    private static Preset ToPreset(PresetDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name ?? "",
        Description = dto.Description ?? "",
        Tweaks = dto.Tweaks ?? new TweakSelection(),
        CreatedAt = dto.CreatedAt ?? TimeStamp.Now(),
        UpdatedAt = dto.UpdatedAt ?? TimeStamp.Now(),
        Deleted = dto.Deleted
    };

    public void Dispose() => _http.Dispose();

    private sealed class AuthResponse
    {
        public string? Token { get; set; }
        public string? ExpiresAt { get; set; }
        public AuthUser? User { get; set; }
    }

    private sealed class AuthUser
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
    }

    private sealed class PresetDto
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string? Description { get; set; }
        public TweakSelection? Tweaks { get; set; }
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public bool Deleted { get; set; }
    }
}
