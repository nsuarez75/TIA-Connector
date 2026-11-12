using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TiaConnector.Client;
using TiaConnector.Contracts;
using Xunit;

namespace TiaConnector.Tests;

public sealed class TiaClientTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<TiaClient> Connect(FakeBridge bridge, int protocol = Protocol.Version)
    {
        bridge.Start(protocol);
        return await TiaClient.ConnectAsync(bridge.ClientReader, bridge.ClientWriter, Timeout);
    }

    [Fact]
    public async Task ConnectAsync_WithProgress_ReportsProgressAndResult()
    {
        var bridge = new FakeBridge((request, b) =>
        {
            b.Send(new Response { Id = request.Id, Kind = MessageKind.Progress, Progress = new ProgressInfo { Stage = ProgressStage.Connecting, Item = "Ejemplo" } });
            b.Result(request, new ProjectInfo { Name = "Ejemplo", Devices = { new DeviceInfo { Name = "PLC_1" } } });
            return true;
        });
        using var client = await Connect(bridge);
        var stages = new List<ProgressStage>();

        var project = await client.ConnectAsync(@"C:\Proyectos\Ejemplo.ap19", OpenMode.Fail, new SyncProgress(p => stages.Add(p.Stage)));

        Assert.Equal("9.9.9", client.BridgeVersion);
        Assert.Equal("PLC_1", project.Devices.Single().Name);
        Assert.Equal(new[] { ProgressStage.Connecting }, stages);
    }

    [Fact]
    public async Task ConnectAsync_BridgeError_ThrowsWithCode()
    {
        var bridge = new FakeBridge((request, b) =>
        {
            b.Error(request, ErrorCode.ProjectNotOpen, "not open");
            return true;
        });
        using var client = await Connect(bridge);

        var error = await Assert.ThrowsAsync<TiaConnectorException>(() => client.ConnectAsync(@"C:\x.ap19"));
        Assert.Equal(ErrorCode.ProjectNotOpen, error.Code);
    }

    [Fact]
    public async Task ReadPlcAsync_SeveralRequests_EachGetsItsAnswer()
    {
        var bridge = new FakeBridge((request, b) =>
        {
            b.Result(request, new PlcTree { Name = Protocol.FromElement<ReadPlcArgs>(request.Args).Plc });
            return true;
        });
        using var client = await Connect(bridge);

        var trees = await Task.WhenAll(client.ReadPlcAsync("PLC_1"), client.ReadPlcAsync("PLC_2"), client.ReadPlcAsync("PLC_3"));

        Assert.Equal(new[] { "PLC_1", "PLC_2", "PLC_3" }, trees.Select(t => t.Name));
    }

    [Fact]
    public async Task Start_OtherProtocol_ThrowsProtocolMismatch()
    {
        var bridge = new FakeBridge((_, _) => true);
        var error = await Assert.ThrowsAsync<TiaConnectorException>(() => Connect(bridge, Protocol.Version + 1));
        Assert.Equal(ErrorCode.ProtocolMismatch, error.Code);
    }

    [Fact]
    public async Task Start_NoOpenness_ThrowsNoOpenness()
    {
        var bridge = new FakeBridge((_, _) => true);
        bridge.Send(new Response { Kind = MessageKind.Error, Code = ErrorCode.NoOpenness, Text = "no TIA" });
        bridge.Close();

        var error = await Assert.ThrowsAsync<TiaConnectorException>(() => TiaClient.ConnectAsync(bridge.ClientReader, bridge.ClientWriter, Timeout));
        Assert.Equal(ErrorCode.NoOpenness, error.Code);
    }

    [Fact]
    public async Task GetOpenProjectsAsync_BridgeStops_ThrowsBridgeExited()
    {
        // El puente se cierra sin contestar.
        var bridge = new FakeBridge((_, _) => false);
        using var client = await Connect(bridge);

        var error = await Assert.ThrowsAsync<TiaConnectorException>(() => client.GetOpenProjectsAsync());
        Assert.Equal(ErrorCode.BridgeExited, error.Code);
        var again = await Assert.ThrowsAsync<TiaConnectorException>(() => client.GetVersionAsync());
        Assert.Equal(ErrorCode.BridgeExited, again.Code);
    }

    [Fact]
    public async Task ModifyBlocksAsync_ChangeFailsInOne_ImportsOthersInTheirFolder()
    {
        var imported = new List<ImportFile>();
        var importedXml = new List<string>();
        var bridge = new FakeBridge((request, b) =>
        {
            switch (request.Op)
            {
                case Operations.ExportBlocks:
                    var export = Protocol.FromElement<ExportBlocksArgs>(request.Args);
                    Assert.Equal(ExportLayout.PerBlock, export.Layout);
                    var result = new BatchResult();
                    for (int i = 0; i < export.Blocks.Count; i++)
                    {
                        string folder = Path.Combine(export.Folder, i.ToString());
                        Directory.CreateDirectory(folder);
                        string path = Path.Combine(folder, export.Blocks[i].Name + ".xml");
                        File.WriteAllText(path, $"<b>{export.Blocks[i].Name}</b>");
                        result.Files.Add(new ExportedFile { Index = i, Item = export.Blocks[i].Name, Path = path });
                    }
                    b.Result(request, result);
                    break;
                case Operations.ImportBlocks:
                    var import = Protocol.FromElement<ImportBlocksArgs>(request.Args);
                    Assert.True(import.CompileAfter);
                    imported.AddRange(import.Files);
                    importedXml.AddRange(import.Files.Select(f => File.ReadAllText(f.Path)));
                    b.Result(request, new BatchResult { Succeeded = import.Files.Count });
                    break;
            }
            return true;
        });
        using var client = await Connect(bridge);
        var blocks = new[]
        {
            new BlockRef { Plc = "PLC_1", Folders = { "Motores" }, Name = "Uno_FB" },
            new BlockRef { Plc = "PLC_2", Folders = { "Otra" }, Name = "Dos_FB" },
        };

        var result = await client.ModifyBlocksAsync(blocks, (block, xml) =>
            block.Name == "Dos_FB" ? throw new InvalidOperationException("no se puede") : Task.FromResult(xml.Replace("<b>", "<b>nuevo ")), compile: true);

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("Dos_FB", result.Failures.Single().Item);
        Assert.Equal("PLC_1", imported.Single().Plc);
        Assert.Equal(new[] { "Motores" }, imported.Single().Folders);
        Assert.Equal("<b>nuevo Uno_FB</b>", importedXml.Single().TrimStart('\uFEFF'));
    }

    [Fact]
    public void InFolder_NestedFolder_ReturnsBlocksWithFolders()
    {
        var tree = new PlcTree
        {
            Name = "PLC_1",
            Root = new BlockFolder
            {
                Name = "Bloques de programa",
                Blocks = { new BlockInfo { Name = "Main" } },
                Folders =
                {
                    new BlockFolder { Name = "Motores", Folders = { new BlockFolder { Name = "Linea1", Blocks = { new BlockInfo { Name = "Uno_FB" } } } } },
                },
            },
        };

        var inMotors = BlockSearch.InFolder(tree, "motores");
        var all = BlockSearch.Find(tree, (_, _) => true);

        Assert.Equal(new[] { "Motores", "Linea1" }, inMotors.Single().Folders);
        Assert.Equal("PLC_1", inMotors.Single().Plc);
        Assert.Equal(new[] { "Main", "Uno_FB" }, all.Select(b => b.Name));
        Assert.Empty(all[0].Folders);
    }

    // Progress<T> pasa por el contexto de sincronización; en las pruebas se quiere al momento.
    private sealed class SyncProgress : IProgress<ProgressInfo>
    {
        private readonly Action<ProgressInfo> _report;

        public SyncProgress(Action<ProgressInfo> report)
        {
            _report = report;
        }

        public void Report(ProgressInfo value) => _report(value);
    }
}
