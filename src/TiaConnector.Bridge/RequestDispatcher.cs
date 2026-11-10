using Siemens.Engineering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TiaConnector.Contracts;
using TiaConnector.Core;

namespace TiaConnector.Bridge;

/// <summary>
/// Atiende cada petición en el hilo de Openness: llama al núcleo y contesta con el resultado o el error. Una petición
/// que falla nunca tumba el puente.
/// </summary>
internal sealed class RequestDispatcher : IDisposable
{
    private readonly ProtocolWriter _writer;
    private readonly TiaSession _session = new();

    public RequestDispatcher(ProtocolWriter writer)
    {
        _writer = writer;
    }

    public void Handle(Request request)
    {
        BridgeLog.Write($"#{request.Id} {request.Op}");
        try
        {
            object? data = Execute(request, new RequestProgress(_writer, request.Id));
            _writer.Result(request.Id, data ?? true);
            BridgeLog.Write($"#{request.Id} ok");
        }
        catch (Exception e)
        {
            var (code, text) = ToError(e);
            BridgeLog.Write($"#{request.Id} {code}: {e}");
            _writer.Error(request.Id, code, text);
        }
    }

    private object? Execute(Request request, IProgress<ProgressInfo> progress)
    {
        switch (request.Op)
        {
            case Operations.Version:
                return new VersionInfo { Bridge = BridgeInfo.Version, Protocol = Protocol.Version, Tia = OpennessAssemblies.TiaVersion };
            case Operations.OpenProjects:
                return TiaSession.OpenProjects();
            case Operations.Connect:
                return _session.Connect(Args<ConnectArgs>(request), progress);
            case Operations.Disconnect:
                _session.Disconnect();
                return null;
            case Operations.ReadTree:
                return ReadTree(Args<ReadTreeArgs>(request), progress);
            case Operations.ReadPlc:
                return Blocks(progress).ReadPlc(Args<ReadPlcArgs>(request).Plc);
            case Operations.ReadHmiBranches:
                return Hmi(progress).ReadBranches(Args<ReadHmiBranchesArgs>(request));
            case Operations.ExportBlocks:
                return Blocks(progress).ExportBlocks(Args<ExportBlocksArgs>(request));
            case Operations.ImportBlocks:
                return Blocks(progress).ImportBlocks(Args<ImportBlocksArgs>(request));
            case Operations.Compile:
                return Blocks(progress).Compile(Args<CompileArgs>(request));
            case Operations.ExportProgram:
                return Blocks(progress).ExportProgram(Args<ExportProgramArgs>(request));
            case Operations.ImportTypes:
                return Blocks(progress).ImportTypes(Args<ImportFilesArgs>(request));
            case Operations.ImportTagTables:
                return Blocks(progress).ImportTagTables(Args<ImportFilesArgs>(request));
            case Operations.ExportHmi:
                return Hmi(progress).Export(Args<ExportHmiArgs>(request));
            case Operations.ImportHmi:
                return Hmi(progress).Import(Args<ImportHmiArgs>(request));
            default:
                throw new TiaConnectorException(ErrorCode.UnknownOperation, $"Unknown operation: {request.Op}");
        }
    }

    private ProjectTree ReadTree(ReadTreeArgs args, IProgress<ProgressInfo> progress)
    {
        var project = _session.Project();
        var plcs = project.Plcs().Where(p => args.Plcs.Contains(p.Name)).ToList();
        var hmis = project.Hmis().Where(h => args.Hmis.Contains(h.Name)).ToList();
        int total = plcs.Count + hmis.Count;
        int current = 0;

        var tree = new ProjectTree();
        foreach (var plc in plcs)
        {
            progress.Report(new ProgressInfo { Stage = ProgressStage.ReadingTree, Current = ++current, Total = total, Item = plc.Name });
            tree.Plcs.Add(BlockOperations.ReadPlc(plc));
        }
        var hmiOperations = new HmiOperations(project, progress);
        foreach (var hmi in hmis)
            tree.Hmis.Add(hmiOperations.ReadHmi(hmi, ++current, total));
        return tree;
    }

    private BlockOperations Blocks(IProgress<ProgressInfo> progress) => new(_session.Project(), progress);

    private HmiOperations Hmi(IProgress<ProgressInfo> progress) => new(_session.Project(), progress);

    private static T Args<T>(Request request)
    {
        try
        {
            return Protocol.FromElement<T>(request.Args);
        }
        catch (JsonException e)
        {
            throw new TiaConnectorException(ErrorCode.InvalidRequest, $"Invalid arguments for {request.Op}: {e.Message}", e);
        }
    }

    internal static (ErrorCode Code, string Text) ToError(Exception e) => e switch
    {
        TiaConnectorException known => (known.Code, known.Message),
        EngineeringSecurityException => (ErrorCode.NoPermission, e.Message),
        EngineeringObjectDisposedException => (ErrorCode.TiaClosed, e.Message),
        ItemFailureException item => (item.Code switch
        {
            FailureCode.NotFound => ErrorCode.NotFound,
            FailureCode.Protected => ErrorCode.Protected,
            FailureCode.DoesNotCompile => ErrorCode.DoesNotCompile,
            FailureCode.PathTooLong => ErrorCode.PathTooLong,
            _ => ErrorCode.Internal,
        }, item.Message),
        _ => (ErrorCode.Internal, e.Message),
    };

    public void Dispose()
    {
        // Solo cierra el TIA sin ventana que abrió el puente; los TIA del usuario se sueltan sin cerrarlos.
        _session.Dispose();
    }
}

internal static class BridgeInfo
{
    public static string Version { get; } =
        typeof(BridgeInfo).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";
}
