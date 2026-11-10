using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;

namespace TiaConnector.Core;

/// <summary>
/// Carga Siemens.Engineering.dll de la instalación de TIA Portal V19: no se copia con el programa. Hay que llamar a
/// <see cref="Register"/> antes de tocar cualquier clase que use Openness.
/// </summary>
public static class OpennessAssemblies
{
    public const string TiaVersion = "V19";

    private const string RegistryKeyPath = @"SOFTWARE\Siemens\Automation\Openness\19.0\PublicAPI\V19";
    private const string StandardPath = @"C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19\Siemens.Engineering.dll";

    private static string? _apiPath;

    /// <summary>Ruta de Siemens.Engineering.dll que se usa, o null si TIA V19 con Openness no está instalado.</summary>
    public static string? ApiPath => _apiPath;

    /// <summary>Busca la API y engancha la carga. Devuelve false si TIA V19 con Openness no está instalado.</summary>
    public static bool Register()
    {
        _apiPath = FindApiPath();
        if (_apiPath == null)
            return false;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveOpenness;
        return true;
    }

    // Según el registro de Openness; si no está registrada, la de una instalación estándar.
    private static string? FindApiPath()
    {
        using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        using (var key = machine.OpenSubKey(RegistryKeyPath))
        {
            if (key?.GetValue("Siemens.Engineering") is string path && File.Exists(path))
                return path;
        }
        return File.Exists(StandardPath) ? StandardPath : null;
    }

    private static Assembly? ResolveOpenness(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name;
        if (_apiPath == null || !name.StartsWith("Siemens.Engineering", StringComparison.OrdinalIgnoreCase))
            return null;
        string path = Path.Combine(Path.GetDirectoryName(_apiPath)!, name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
