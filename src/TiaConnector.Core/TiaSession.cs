using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using TiaConnector.Contracts;
using OpenMode = TiaConnector.Contracts.OpenMode;

namespace TiaConnector.Core;

/// <summary>
/// Conexión con un proyecto de TIA Portal. Solo se usa desde el hilo de Openness del puente.
/// <para>
/// Norma de seguridad: nunca se hace Dispose de un TiaPortalProcess ni de un TiaPortal enganchado del usuario, porque
/// cerraría su TIA. Solo se cierra el TIA sin ventana que abre la propia sesión.
/// </para>
/// </summary>
public sealed class TiaSession : IDisposable
{
    private static readonly TimeSpan OpenWithWindowTimeout = TimeSpan.FromMinutes(10);

    // TIA del usuario enganchados: se guardan para que no se suelten mientras dura la sesión.
    private readonly List<TiaPortal> _attached = new();

    // TIA sin ventana abierto por la sesión: el único que se cierra.
    private TiaPortal? _ownPortal;

    private Project? _project;
    private string? _projectPath;

    /// <summary>Rutas de los proyectos abiertos en los TIA que están en marcha.</summary>
    public static List<OpenProject> OpenProjects() =>
        TiaPortal.GetProcesses()
            .Where(p => p.ProjectPath != null)
            .Select(p => new OpenProject { ProcessId = p.Id, ProjectPath = p.ProjectPath.FullName })
            .ToList();

    /// <summary>
    /// Se engancha al TIA que tiene abierto el proyecto o, si no está abierto en ninguno, lo abre según
    /// <paramref name="args"/>. Desconecta antes del proyecto anterior, si lo había.
    /// </summary>
    public ProjectInfo Connect(ConnectArgs args, IProgress<ProgressInfo> progress)
    {
        if (string.IsNullOrWhiteSpace(args.ProjectPath))
            throw new TiaConnectorException(ErrorCode.InvalidRequest, "projectPath is required.");
        Disconnect();

        string path = Path.GetFullPath(args.ProjectPath);
        progress.Report(new ProgressInfo { Stage = ProgressStage.Connecting, Item = Path.GetFileNameWithoutExtension(path) });
        bool openedByBridge = false;
        var project = AttachTo(path);
        if (project == null)
        {
            if (args.IfNotOpen == OpenMode.Fail)
                throw new TiaConnectorException(ErrorCode.ProjectNotOpen, $"The project is not open in any TIA Portal: {path}");
            if (!File.Exists(path))
                throw new TiaConnectorException(ErrorCode.NotFound, $"The project does not exist: {path}");
            progress.Report(new ProgressInfo { Stage = ProgressStage.OpeningProject, Item = Path.GetFileNameWithoutExtension(path) });
            project = args.IfNotOpen == OpenMode.WithoutWindow ? OpenWithoutWindow(path) : OpenWithWindow(path);
            openedByBridge = args.IfNotOpen == OpenMode.WithoutWindow;
        }

        _project = project;
        _projectPath = path;
        var access = new ProjectAccess(project);
        return new ProjectInfo
        {
            Name = project.Name,
            ProjectPath = project.Path.FullName,
            ProjectFolder = project.Path.DirectoryName,
            EditingLanguage = project.LanguageSettings.EditingLanguage?.Culture?.Name ?? "",
            OpenedByBridge = openedByBridge,
            Devices = access.Plcs().Select(p => new DeviceInfo { Name = p.Name, Kind = DeviceKind.Plc })
                .Concat(access.Hmis().Select(h => new DeviceInfo { Name = h.Name, Kind = DeviceKind.Hmi }))
                .ToList(),
        };
    }

    /// <summary>Suelta el proyecto. Si lo abrió la sesión en un TIA sin ventana, lo cierra sin guardar.</summary>
    public void Disconnect()
    {
        _project = null;
        _projectPath = null;
        _attached.Clear();
        CloseOwnPortal();
    }

    /// <summary>
    /// El proyecto conectado, comprobando que sigue abierto. Si el usuario lo cerró, lanza ProjectNotOpen.
    /// </summary>
    public ProjectAccess Project()
    {
        if (_project == null || _projectPath == null)
            throw new TiaConnectorException(ErrorCode.NotConnected, "Connect to a project first.");
        if (!IsAlive(_project))
        {
            // Puede que el usuario lo haya cerrado y vuelto a abrir: se intenta enganchar de nuevo.
            _project = AttachTo(_projectPath)
                ?? throw new TiaConnectorException(ErrorCode.ProjectNotOpen, "The project is no longer open in TIA Portal.");
        }
        return new ProjectAccess(_project);
    }

    public static bool IsAlive(Project project)
    {
        try
        {
            _ = project.Name;
            return true;
        }
        catch (EngineeringObjectDisposedException)
        {
            return false;
        }
    }

    private Project? AttachTo(string path)
    {
        var process = TiaPortal.GetProcesses().FirstOrDefault(p => p.ProjectPath != null && SamePath(p.ProjectPath.FullName, path));
        if (process == null)
            return null;

        TiaPortal portal;
        try
        {
            // La primera vez TIA pregunta al usuario si deja entrar a este programa.
            portal = process.Attach();
        }
        catch (EngineeringSecurityException e)
        {
            throw new TiaConnectorException(ErrorCode.NoPermission,
                "TIA Portal did not allow the connection (the user rejected it or is not in the 'Siemens TIA Openness' group).", e);
        }
        _attached.Add(portal);
        return portal.Projects.FirstOrDefault(p => SamePath(p.Path.FullName, path))
            ?? throw new TiaConnectorException(ErrorCode.ProjectNotOpen, "Could not access the project open in TIA Portal.");
    }

    private Project OpenWithoutWindow(string path)
    {
        var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
        try
        {
            var project = portal.Projects.Open(new FileInfo(path));
            _ownPortal = portal;
            return project;
        }
        catch
        {
            portal.Dispose();
            throw;
        }
    }

    // Como si el usuario hiciera doble clic en el proyecto: ese TIA no depende del puente y sigue abierto cuando
    // termina. Se espera a que tenga el proyecto cargado y se engancha.
    private Project OpenWithWindow(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        var limit = DateTime.UtcNow + OpenWithWindowTimeout;
        while (DateTime.UtcNow < limit)
        {
            Thread.Sleep(2000);
            try
            {
                if (AttachTo(path) is { } project)
                    return project;
            }
            catch (TiaConnectorException e) when (e.Code == ErrorCode.ProjectNotOpen)
            {
                // TIA aún está cargando el proyecto.
            }
            catch (EngineeringException)
            {
                // TIA aún está arrancando.
            }
        }
        throw new TiaConnectorException(ErrorCode.ProjectNotOpen, "TIA Portal did not finish opening the project in time.");
    }

    private void CloseOwnPortal()
    {
        var own = _ownPortal;
        _ownPortal = null;
        own?.Dispose();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    public void Dispose() => Disconnect();
}
