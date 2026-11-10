using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TiaConnector.Contracts;

namespace TiaConnector.Client;

/// <summary>Opciones para arrancar el puente.</summary>
public sealed class TiaClientOptions
{
    /// <summary>Ruta de un puente ya extraído (pruebas y desarrollo). Si es null, se usa el que lleva dentro el cliente.</summary>
    public string? BridgePath { get; set; }

    /// <summary>Cuánto se espera a que el puente arranque.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Cuánto se espera al cerrar a que el puente termine lo pendiente y cierre su TIA sin ventana; después se mata.
    /// </summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// Lo que usan las apps para trabajar con TIA Portal. Arranca su propio puente (un proceso .NET Framework 4.8) y le
/// pide las operaciones; todas son asíncronas y se atienden de una en una, en el orden en que se piden. Al hacer
/// Dispose, el puente cierra el TIA sin ventana que haya abierto (nunca el del usuario) y termina.
/// </summary>
public sealed class TiaClient : IDisposable
{
    private readonly BridgeChannel _channel;
    private readonly Process? _process;
    private readonly TimeSpan _stopTimeout;

    private TiaClient(BridgeChannel channel, Process? process, TimeSpan stopTimeout, string bridgeVersion)
    {
        _channel = channel;
        _process = process;
        _stopTimeout = stopTimeout;
        BridgeVersion = bridgeVersion;
        _channel.Warning += text => Warning?.Invoke(text);
    }

    /// <summary>Versión del puente con el que se habla.</summary>
    public string BridgeVersion { get; }

    /// <summary>Avisos del puente que no son de ninguna petición concreta (en inglés, para el registro de la app).</summary>
    public event Action<string>? Warning;

    /// <summary>
    /// Extrae y arranca el puente. Falla con NoOpenness si el equipo no tiene TIA Portal V19 con Openness.
    /// </summary>
    public static async Task<TiaClient> StartAsync(TiaClientOptions? options = null)
    {
        options ??= new TiaClientOptions();
        string path = options.BridgePath ?? BridgeProcess.Extract();
        var process = BridgeProcess.Start(path);
        var channel = new BridgeChannel(process.StandardOutput,
            new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)) { NewLine = "\n" });
        try
        {
            var ready = await channel.WaitReadyAsync(options.StartTimeout).ConfigureAwait(false);
            return new TiaClient(channel, process, options.StopTimeout, ready.BridgeVersion ?? "");
        }
        catch
        {
            channel.Dispose();
            if (!process.HasExited)
                process.Kill();
            process.Dispose();
            throw;
        }
    }

    /// <summary>Cliente sobre flujos ya abiertos, sin proceso (pruebas con un puente falso).</summary>
    internal static async Task<TiaClient> ConnectAsync(TextReader fromBridge, TextWriter toBridge, TimeSpan timeout)
    {
        var channel = new BridgeChannel(fromBridge, toBridge);
        var ready = await channel.WaitReadyAsync(timeout).ConfigureAwait(false);
        return new TiaClient(channel, null, TimeSpan.Zero, ready.BridgeVersion ?? "");
    }

    // ------------------------------------------------------------------ Sesión y proyecto

    public Task<VersionInfo> GetVersionAsync(CancellationToken cancellation = default) =>
        Send<VersionInfo>(Operations.Version, null, null, cancellation);

    /// <summary>Proyectos abiertos en los TIA en marcha. No se engancha a ninguno.</summary>
    public Task<List<OpenProject>> GetOpenProjectsAsync(CancellationToken cancellation = default) =>
        Send<List<OpenProject>>(Operations.OpenProjects, null, null, cancellation);

    /// <summary>
    /// Se engancha al TIA que tiene abierto el proyecto (la primera vez TIA pregunta al usuario) o, si no está abierto,
    /// hace lo que diga <paramref name="ifNotOpen"/>. Devuelve sus PLC y HMI, sin lo que tienen dentro.
    /// </summary>
    public Task<ProjectInfo> ConnectAsync(string projectPath, OpenMode ifNotOpen = OpenMode.Fail,
        IProgress<ProgressInfo>? progress = null, CancellationToken cancellation = default) =>
        Send<ProjectInfo>(Operations.Connect, new ConnectArgs { ProjectPath = projectPath, IfNotOpen = ifNotOpen }, progress, cancellation);

    /// <summary>Suelta el proyecto; si el puente lo abrió sin ventana, lo cierra sin guardar.</summary>
    public Task DisconnectAsync(CancellationToken cancellation = default) =>
        Send<bool>(Operations.Disconnect, null, null, cancellation);

    // ------------------------------------------------------------------ Leer

    public Task<ProjectTree> ReadTreeAsync(IEnumerable<string> plcs, IEnumerable<string> hmis,
        IProgress<ProgressInfo>? progress = null, CancellationToken cancellation = default) =>
        Send<ProjectTree>(Operations.ReadTree, new ReadTreeArgs { Plcs = plcs.ToList(), Hmis = hmis.ToList() }, progress, cancellation);

    public Task<PlcTree> ReadPlcAsync(string plc, CancellationToken cancellation = default) =>
        Send<PlcTree>(Operations.ReadPlc, new ReadPlcArgs { Plc = plc }, null, cancellation);

    public Task<List<HmiBranchTree>> ReadHmiBranchesAsync(string hmi, IEnumerable<HmiBranch> branches,
        CancellationToken cancellation = default) =>
        Send<List<HmiBranchTree>>(Operations.ReadHmiBranches, new ReadHmiBranchesArgs { Hmi = hmi, Branches = branches.ToList() }, null, cancellation);

    /// <summary>Bloques con ese nombre (sin distinguir mayúsculas) en todos los PLC del proyecto.</summary>
    public async Task<List<BlockRef>> FindBlocksByNameAsync(ProjectInfo project, string name, CancellationToken cancellation = default)
    {
        var plcs = project.Devices.Where(d => d.Kind == DeviceKind.Plc).Select(d => d.Name).ToList();
        var tree = await ReadTreeAsync(plcs, Array.Empty<string>(), null, cancellation).ConfigureAwait(false);
        string wanted = name.Trim();
        return tree.Plcs.SelectMany(p => BlockSearch.Find(p, (_, block) => block.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    // ------------------------------------------------------------------ Bloques

    public Task<BatchResult> ExportBlocksAsync(ExportBlocksArgs args, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ExportBlocks, args, progress, cancellation);

    public Task<BatchResult> ImportBlocksAsync(ImportBlocksArgs args, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ImportBlocks, args, progress, cancellation);

    public Task<CompileResult> CompileAsync(CompileArgs args, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<CompileResult>(Operations.Compile, args, progress, cancellation);

    /// <summary>Programa entero de un PLC en Bloques, Variables y Tipos, sin compilar nada.</summary>
    public Task<BatchResult> ExportProgramAsync(string plc, string folder, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ExportProgram, new ExportProgramArgs { Plc = plc, Folder = folder }, progress, cancellation);

    public Task<BatchResult> ImportTypesAsync(IEnumerable<ImportFile> files, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ImportTypes, new ImportFilesArgs { Files = files.ToList() }, progress, cancellation);

    public Task<BatchResult> ImportTagTablesAsync(IEnumerable<ImportFile> files, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ImportTagTables, new ImportFilesArgs { Files = files.ToList() }, progress, cancellation);

    /// <summary>XML de un bloque sin dejar nada en disco (se exporta a una carpeta temporal que se borra).</summary>
    public async Task<string> ReadBlockXmlAsync(BlockRef block, CancellationToken cancellation = default)
    {
        using var temp = new TempFolder();
        var result = await ExportBlocksAsync(new ExportBlocksArgs { Blocks = { block }, Folder = temp.Path, Layout = ExportLayout.PerBlock },
            null, cancellation).ConfigureAwait(false);
        if (result.Files.Count == 0)
        {
            var failure = result.Failures.FirstOrDefault();
            throw new TiaConnectorException(FailureToError(failure?.Code), failure?.Detail ?? "The block could not be exported.");
        }
        return File.ReadAllText(result.Files[0].Path);
    }

    /// <summary>
    /// Exporta los bloques a una carpeta temporal, aplica a cada XML el cambio de la app (recibe el bloque y el XML y
    /// devuelve el XML nuevo; puede ser asíncrono, por ejemplo para traducir), los importa encima y borra la carpeta.
    /// Si el cambio falla en un bloque, ese no se importa y se sigue con los demás.
    /// </summary>
    public async Task<BatchResult> ModifyBlocksAsync(IReadOnlyList<BlockRef> blocks, Func<BlockRef, string, Task<string>> change,
        bool compile, IProgress<ProgressInfo>? progress = null, CancellationToken cancellation = default)
    {
        using var temp = new TempFolder();
        var exported = await ExportBlocksAsync(new ExportBlocksArgs { Blocks = blocks.ToList(), Folder = temp.Path, Layout = ExportLayout.PerBlock },
            progress, cancellation).ConfigureAwait(false);

        // Exportar no es el trabajo: de la exportación solo cuentan los fallos; los correctos se cuentan al importar.
        var result = new BatchResult { Failures = exported.Failures.ToList(), Stopped = exported.Stopped, Remaining = exported.Remaining };
        if (exported.Stopped)
            return result;

        var toImport = new List<ImportFile>();
        foreach (var file in exported.Files)
        {
            var block = blocks[file.Index];
            try
            {
                string xml = File.ReadAllText(file.Path);
                File.WriteAllText(file.Path, await change(block, xml).ConfigureAwait(false), new UTF8Encoding(true));
                toImport.Add(new ImportFile { Path = file.Path, Plc = block.Plc, Folders = block.Folders.ToList() });
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                result.Failures.Add(new ItemFailure { Item = block.Name, Code = FailureCode.Other, Detail = e.Message });
            }
        }

        var imported = await ImportBlocksAsync(new ImportBlocksArgs { Files = toImport, CompileAfter = compile }, progress, cancellation)
            .ConfigureAwait(false);
        result.Succeeded = imported.Succeeded;
        result.Failures.AddRange(imported.Failures);
        result.Stopped = imported.Stopped;
        result.Remaining = imported.Remaining;
        return result;
    }

    // ------------------------------------------------------------------ HMI

    public Task<BatchResult> ExportHmiAsync(ExportHmiArgs args, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ExportHmi, args, progress, cancellation);

    public Task<BatchResult> ImportHmiAsync(ImportHmiArgs args, IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellation = default) =>
        Send<BatchResult>(Operations.ImportHmi, args, progress, cancellation);

    // ------------------------------------------------------------------ Comunes

    private Task<T> Send<T>(string operation, object? args, IProgress<ProgressInfo>? progress, CancellationToken cancellation) =>
        _channel.SendAsync<T>(operation, args, progress, cancellation);

    private static ErrorCode FailureToError(FailureCode? code) => code switch
    {
        FailureCode.Protected => ErrorCode.Protected,
        FailureCode.NotFound => ErrorCode.NotFound,
        FailureCode.DoesNotCompile => ErrorCode.DoesNotCompile,
        FailureCode.PathTooLong => ErrorCode.PathTooLong,
        _ => ErrorCode.Internal,
    };

    /// <summary>
    /// Cierra la entrada del puente: termina lo pendiente, cierra su TIA sin ventana (si abrió uno) y sale. Si no sale
    /// a tiempo, se mata.
    /// </summary>
    public void Dispose()
    {
        _channel.Dispose();
        if (_process == null)
            return;
        try
        {
            if (!_process.WaitForExit((int)_stopTimeout.TotalMilliseconds))
                _process.Kill();
        }
        catch (InvalidOperationException)
        {
            // Ya había terminado.
        }
        _process.Dispose();
    }
}

/// <summary>Búsquedas en un árbol de bloques ya leído, sin pedir nada al puente.</summary>
public static class BlockSearch
{
    /// <summary>
    /// Bloques del árbol que cumplen el filtro (recibe las carpetas desde la raíz y el bloque), en el orden del árbol.
    /// </summary>
    public static List<BlockRef> Find(PlcTree plc, Func<IReadOnlyList<string>, BlockInfo, bool> filter)
    {
        var found = new List<BlockRef>();
        Walk(plc.Root, new List<string>(), (folders, block) =>
        {
            if (filter(folders, block))
                found.Add(new BlockRef { Plc = plc.Name, Folders = folders.ToList(), Name = block.Name, IsProtected = block.IsProtected });
        });
        return found;
    }

    /// <summary>Bloques de cualquier subcarpeta (a cualquier profundidad) con ese nombre.</summary>
    public static List<BlockRef> InFolder(PlcTree plc, string folderName) =>
        Find(plc, (folders, _) => folders.Any(f => f.Equals(folderName, StringComparison.OrdinalIgnoreCase)));

    // La raíz es la carpeta de bloques del PLC: su nombre no forma parte de la ruta.
    private static void Walk(BlockFolder folder, List<string> path, Action<IReadOnlyList<string>, BlockInfo> onBlock)
    {
        foreach (var block in folder.Blocks)
            onBlock(path, block);
        foreach (var sub in folder.Folders)
        {
            path.Add(sub.Name);
            Walk(sub, path, onBlock);
            path.RemoveAt(path.Count - 1);
        }
    }
}
