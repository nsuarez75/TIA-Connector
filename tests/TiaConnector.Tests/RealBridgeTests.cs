using System.Threading.Tasks;
using TiaConnector.Client;
using TiaConnector.Contracts;
using Xunit;

namespace TiaConnector.Tests;

/// <summary>
/// El puente de verdad que lleva dentro el cliente: se extrae, arranca solo (sin su .config al lado) y contesta. No se
/// engancha a ningún TIA. En un equipo sin TIA V19 vale con que diga NoOpenness.
/// </summary>
public sealed class RealBridgeTests
{
    [Fact]
    public async Task StartAsync_EmbeddedBridge_StartsOrReportsNoOpenness()
    {
        TiaClient client;
        try
        {
            client = await TiaClient.StartAsync();
        }
        catch (TiaConnectorException e) when (e.Code == ErrorCode.NoOpenness)
        {
            return;
        }

        using (client)
        {
            var version = await client.GetVersionAsync();
            Assert.Equal(Protocol.Version, version.Protocol);
            Assert.Equal("V19", version.Tia);
            Assert.Equal(client.BridgeVersion, version.Bridge);
        }
    }

    [Fact]
    public async Task Request_BeforeConnect_FailsAndBridgeKeepsWorking()
    {
        TiaClient client;
        try
        {
            client = await TiaClient.StartAsync();
        }
        catch (TiaConnectorException e) when (e.Code == ErrorCode.NoOpenness)
        {
            return;
        }

        using (client)
        {
            var error = await Assert.ThrowsAsync<TiaConnectorException>(() => client.ReadPlcAsync("PLC_que_no_existe"));
            Assert.Equal(ErrorCode.NotConnected, error.Code);
            Assert.Equal(Protocol.Version, (await client.GetVersionAsync()).Protocol);
        }
    }
}
