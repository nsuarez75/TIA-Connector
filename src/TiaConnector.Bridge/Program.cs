using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TiaConnector.Contracts;
using TiaConnector.Core;

namespace TiaConnector.Bridge;

/// <summary>
/// Puente entre las apps y TIA Portal. Lee peticiones (una línea JSON cada una) por la entrada estándar y contesta por
/// la salida estándar. Al cerrarse la entrada estándar, cierra el TIA sin ventana que haya abierto y termina.
/// </summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitNoOpenness = 2;

    [STAThread]
    private static int Main()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var writer = new ProtocolWriter(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false });
        // Nada más puede escribir en la salida estándar: es el canal del protocolo.
        Console.SetOut(TextWriter.Null);
        BridgeLog.Start();
        BridgeLog.Write($"start {BridgeInfo.Version}");

        // Antes de tocar cualquier clase que use Openness.
        if (!OpennessAssemblies.Register())
        {
            BridgeLog.Write("TIA Portal V19 with Openness is not installed");
            writer.Error(null, ErrorCode.NoOpenness, "TIA Portal V19 with Openness is not installed.");
            return ExitNoOpenness;
        }
        BridgeLog.Write($"openness {OpennessAssemblies.ApiPath}");

        var thread = new OpennessThread();
        var dispatcher = new RequestDispatcher(writer);
        writer.Send(new Response { Kind = MessageKind.Ready, Protocol = Protocol.Version, BridgeVersion = BridgeInfo.Version });

        using (var input = new StreamReader(Console.OpenStandardInput(), utf8))
        {
            string? line;
            while ((line = input.ReadLine()) != null)
            {
                if (line.Trim().Length == 0)
                    continue;
                Request request;
                try
                {
                    request = Protocol.FromLine<Request>(line);
                }
                catch (JsonException e)
                {
                    BridgeLog.Write($"invalid request: {e.Message}");
                    writer.Error(null, ErrorCode.InvalidRequest, $"Invalid request: {e.Message}");
                    continue;
                }
                thread.Enqueue(() => dispatcher.Handle(request));
            }
        }

        // El cliente ha cerrado: se acaba lo pendiente y se cierra solo lo abierto por el puente.
        thread.Enqueue(dispatcher.Dispose);
        thread.CompleteAndWait();
        thread.Dispose();
        BridgeLog.Write("end");
        return ExitOk;
    }
}
