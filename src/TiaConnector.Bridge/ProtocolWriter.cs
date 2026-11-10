using System;
using System.IO;
using TiaConnector.Contracts;

namespace TiaConnector.Bridge;

/// <summary>
/// Escribe los mensajes al cliente por la salida estándar, una línea JSON cada uno. Es lo único que escribe ahí: el
/// resto de Console.Out se desvía para que nada ajeno (Openness, librerías) rompa el protocolo.
/// </summary>
internal sealed class ProtocolWriter
{
    private readonly TextWriter _output;
    private readonly object _lock = new();

    public ProtocolWriter(TextWriter output)
    {
        _output = output;
    }

    public void Send(Response response)
    {
        string line = Protocol.ToLine(response);
        lock (_lock)
        {
            _output.Write(line);
            _output.Write('\n');
            _output.Flush();
        }
    }

    public void Progress(long id, ProgressInfo progress) =>
        Send(new Response { Id = id, Kind = MessageKind.Progress, Progress = progress });

    public void Result<T>(long id, T data) =>
        Send(new Response { Id = id, Kind = MessageKind.Result, Data = Protocol.ToElement(data) });

    public void Error(long? id, ErrorCode code, string text) =>
        Send(new Response { Id = id, Kind = MessageKind.Error, Code = code, Text = text });
}

/// <summary>Progreso que se escribe al momento como mensaje de la petición (sin pasar por otro hilo).</summary>
internal sealed class RequestProgress : IProgress<ProgressInfo>
{
    private readonly ProtocolWriter _writer;
    private readonly long _id;

    public RequestProgress(ProtocolWriter writer, long id)
    {
        _writer = writer;
        _id = id;
    }

    public void Report(ProgressInfo value) => _writer.Progress(_id, value);
}
