using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;

namespace GameBooster.Services;

public static class AppJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public static class TimeStamp
{
    private static readonly object Gate = new();
    private static long _lastUnixMs;

    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string Touch(string? previous = null)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var floor = nowMs;
        if (TryParse(previous, out var prev))
        {
            var prevMs = new DateTimeOffset(prev.ToUniversalTime()).ToUnixTimeMilliseconds();
            if (prevMs + 1 > floor)
                floor = prevMs + 1;
        }
        lock (Gate)
        {
            if (_lastUnixMs + 1 > floor)
                floor = _lastUnixMs + 1;
            _lastUnixMs = floor;
        }
        return Format(DateTimeOffset.FromUnixTimeMilliseconds(floor).UtcDateTime);
    }

    public static string? EnsureIso(string? value)
    {
        if (!TryParse(value, out var utc))
            return null;
        return Format(utc.ToUniversalTime());
    }

    private static string Format(DateTime utc)
        => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static bool TryParse(string? value, out DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            utc = default;
            return false;
        }
        return DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out utc);
    }

    public static int Compare(string? a, string? b)
    {
        var okA = TryParse(a, out var da);
        var okB = TryParse(b, out var db);
        if (!okA && !okB) return 0;
        if (!okA) return -1;
        if (!okB) return 1;
        return da.CompareTo(db);
    }

    public static string Local(string? iso)
    {
        if (!TryParse(iso, out var utc) || utc.Year < 1971)
            return "—";
        return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
    }
}

public static class Dialogs
{
    public static bool Confirm(string text)
        => MessageBox.Show(text, "Game Booster", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public static void Info(string text)
        => MessageBox.Show(text, "Game Booster", MessageBoxButton.OK, MessageBoxImage.Information);

    public static void Error(string text)
        => MessageBox.Show(text, "Game Booster", MessageBoxButton.OK, MessageBoxImage.Error);
}

public static partial class Guard
{
    public static bool IsPlaceholder(string? url)
        => string.IsNullOrWhiteSpace(url) || url.Contains("<subdomain>", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[a-z0-9_]{3,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();

    public static string? ValidateUsername(string? username)
    {
        var value = (username ?? "").Trim().ToLowerInvariant();
        if (!UsernamePattern().IsMatch(value))
            return "Le nom d'utilisateur doit contenir 3 à 20 caractères (lettres, chiffres, _).";
        return null;
    }

    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8 || password.Length > 128)
            return "Le mot de passe doit contenir entre 8 et 128 caractères.";
        return null;
    }

    public static string NormalizeApiBase(string? url)
    {
        var value = (url ?? "").Trim().TrimEnd('/');
        return value;
    }
}

public sealed class ApiException : Exception
{
    public int Status { get; }
    public string Raw { get; }

    public ApiException(int status, string message, string raw) : base(message)
    {
        Status = status;
        Raw = raw;
    }
}

public static class ErrorText
{
    public static string French(Exception ex)
    {
        if (ex is ApiException api)
        {
            if (api.Status == 401)
                return "Session expirée. Reconnectez-vous.";
            if (api.Status == 429)
                return "Trop de tentatives. Réessayez dans quelques minutes.";
            if (!string.IsNullOrWhiteSpace(api.Message))
                return api.Message;
            return "Le serveur a répondu " + api.Status + ".";
        }
        if (ex is HttpRequestException or TaskCanceledException)
            return "Impossible de joindre le serveur. Vérifiez l'URL de l'API et la connexion.";
        return "Une erreur est survenue.";
    }
}
