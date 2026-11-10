using System.Linq;
using TiaConnector.Contracts;
using Xunit;

namespace TiaConnector.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void Request_is_one_line_with_camelCase_names_and_enums_as_text()
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
    public void Response_round_trips_with_its_data()
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
    public void Missing_data_is_an_error_not_an_empty_object() =>
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Protocol.FromElement<BatchResult>(null));

    [Fact]
    public void BlockRef_prints_its_whole_path() =>
        Assert.Equal("PLC_1/Carpeta/Sub/Bloque_FB",
            new BlockRef { Plc = "PLC_1", Folders = { "Carpeta", "Sub" }, Name = "Bloque_FB" }.ToString());
}
