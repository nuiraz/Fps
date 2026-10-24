using System.Globalization;
using System.Text;

namespace GameBooster.Services;

public sealed class AppLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public AppLog()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBooster", "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "app.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null)
    {
        var text = ex == null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message;
        Write("ERROR", text);
    }

    private void Write(string level, string message)
    {
        var line = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("fr-FR"))
            + " " + level + " " + message.Replace('\r', ' ').Replace('\n', ' ');
        lock (_gate)
        {
            try
            {
                var info = new FileInfo(_path);
                if (info.Exists && info.Length > 1024 * 1024)
                {
                    var previous = _path + ".1";
                    File.Delete(previous);
                    File.Move(_path, previous);
                }
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Le journal ne doit pas interrompre l'application.
            }
        }
    }
}
