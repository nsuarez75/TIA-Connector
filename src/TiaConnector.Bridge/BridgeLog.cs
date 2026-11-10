using System;
using System.IO;
using System.Linq;

namespace TiaConnector.Bridge;

/// <summary>
/// Registro del puente para diagnosticar fallos: %LOCALAPPDATA%\TIA-Connector\logs\bridge-AAAAMMDD.log, un archivo
/// por día; los de más de 14 días se borran al arrancar. Solo operaciones, nombres de elementos y errores: nunca el
/// contenido de los proyectos. Si no se puede escribir, el puente sigue sin registro.
/// </summary>
internal static class BridgeLog
{
    private const int DaysToKeep = 14;

    private static readonly object Lock = new();
    private static readonly int ProcessId = System.Diagnostics.Process.GetCurrentProcess().Id;
    private static string? _folder;

    public static void Start()
    {
        try
        {
            _folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TIA-Connector", "logs");
            Directory.CreateDirectory(_folder);
            var limit = DateTime.Now.AddDays(-DaysToKeep);
            foreach (var old in new DirectoryInfo(_folder).GetFiles("bridge-*.log").Where(f => f.LastWriteTime < limit))
                old.Delete();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            _folder = null;
        }
    }

    public static void Write(string text)
    {
        if (_folder == null)
            return;
        lock (Lock)
        {
            try
            {
                string path = Path.Combine(_folder, $"bridge-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [{ProcessId}] {text}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // Sin registro, el puente sigue funcionando.
            }
        }
    }
}
