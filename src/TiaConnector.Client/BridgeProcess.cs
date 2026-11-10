using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TiaConnector.Contracts;

namespace TiaConnector.Client;

/// <summary>
/// Saca el puente que lleva dentro el cliente a %LOCALAPPDATA%\TIA-Connector\bridge\&lt;versión&gt;-&lt;huella&gt;\ y lo
/// arranca. Cada versión va en su carpeta: dos apps con versiones distintas de TIA-Connector conviven.
/// </summary>
internal static class BridgeProcess
{
    private const string ResourceName = "TiaConnector.Bridge.exe";

    /// <summary>Ruta del puente extraído; lo extrae si no está o si no está entero.</summary>
    public static string Extract()
    {
        using var resource = typeof(BridgeProcess).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new TiaConnectorException(ErrorCode.BridgeExited, "The bridge is not embedded in TiaConnector.Client.");
        byte[] bytes = ReadAll(resource);
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TIA-Connector", "bridge", $"{ClientVersion()}-{Fingerprint(bytes)}");
        string path = Path.Combine(folder, ResourceName);
        if (File.Exists(path) && new FileInfo(path).Length == bytes.Length)
            return path;

        Directory.CreateDirectory(folder);
        // Primero a un archivo aparte y luego se renombra: otra app que arranque a la vez nunca ve un .exe a medias.
        string partial = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(partial, bytes);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
            File.Move(partial, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Otra app lo ha dejado a la vez; vale el suyo.
            File.Delete(partial);
        }
        return path;
    }

    public static Process Start(string bridgePath)
    {
        var info = new ProcessStartInfo(bridgePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(bridgePath),
        };
        var process = Process.Start(info) ?? throw new TiaConnectorException(ErrorCode.BridgeExited, "Could not start the bridge.");
        // La salida de errores no forma parte del protocolo; se lee para que no se llene y bloquee el puente.
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return process;
    }

    private static string ClientVersion() =>
        typeof(BridgeProcess).Assembly.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    private static string Fingerprint(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Take(4).Select(b => b.ToString("x2")));
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
