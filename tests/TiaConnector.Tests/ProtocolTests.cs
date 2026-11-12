using System.Linq;
using TiaConnector.Contracts;
using Xunit;

namespace TiaConnector.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void ToLine_Request_OneLineInCamelCase()
    {
        var request = new Request
        {
            Id = 7,
            Op = Operations.ExportBlocks,
            Args = Protocol.ToElement(new ExportBlocksArgs { Folder = "C:\\x", Layout = ExportLayout.PerBlock }),
        };
        string line = Protocol.ToLine(request);

        Assert.DoesNotContain("\n", line);
        Assert.Contains("\"op\":\"exportBlocks\"", line);
        Assert.Contains("\"layout\":\"perBlock\"", line);
    }

    [Fact]
    public void FromLine_Response_KeepsData()
    {
        var result = new BatchResult { Succeeded = 2, Failures = { new ItemFailure { Item = "A_FB", Code = FailureCode.Protected } } };
        var response = new Response { Id = 3, Kind = MessageKind.Result, Data = Protocol.ToElement(result) };

        var read = Protocol.FromLine<Response>(Protocol.ToLine(response));
        var data = Protocol.FromElement<BatchResult>(read.Data);

        Assert.Equal(MessageKind.Result, read.Kind);
        Assert.Equal(2, data.Succeeded);
        Assert.Equal(FailureCode.Protected, data.Failures.Single().Code);
    }

    [Fact]
    public void FromElement_DeviceStation_KeptOrEmpty()
    {
        var info = new ProjectInfo { Devices = { new DeviceInfo { Name = "HMI_RT_1", Kind = DeviceKind.Hmi, Station = "IPC_Linea1" } } };

        var read = Protocol.FromElement<ProjectInfo>(Protocol.ToElement(info));
        var older = Protocol.FromLine<DeviceInfo>("{\"name\":\"PLC_1\",\"kind\":\"plc\"}");

        Assert.Equal("IPC_Linea1", read.Devices.Single().Station);
        Assert.Equal("", older.Station);
    }

    [Fact]
    public void FromElement_NullData_Throws() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Protocol.FromElement<BatchResult>(null));

    [Fact]
    public void ToString_BlockRef_ReturnsWholePath() =>
        Assert.Equal("PLC_1/Carpeta/Sub/Bloque_FB",
            new BlockRef { Plc = "PLC_1", Folders = { "Carpeta", "Sub" }, Name = "Bloque_FB" }.ToString());
}
