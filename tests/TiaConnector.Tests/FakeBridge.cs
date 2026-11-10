using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TiaConnector.Contracts;

namespace TiaConnector.Tests;

/// <summary>
/// Puente falso en memoria para probar el cliente sin TIA: recibe las peticiones del cliente y contesta lo que diga la
/// prueba.
/// </summary>
internal sealed class FakeBridge
{
    private readonly LineQueue _toClient = new();
    private readonly LineQueue _toBridge = new();
    private readonly Func<Request, FakeBridge, bool> _handler;

    /// <param name="handler">Atiende una petición escribiendo respuestas con <see cref="Send"/>; devuelve false para cerrar.</param>
    public FakeBridge(Func<Request, FakeBridge, bool> handler)
    {
        _handler = handler;
    }

    public TextReader ClientReader => new QueueReader(_toClient);
    public TextWriter ClientWriter => new QueueWriter(_toBridge);

    public void Start(int protocol = Protocol.Version)
    {
        Send(new Response { Kind = MessageKind.Ready, Protocol = protocol, BridgeVersion = "9.9.9" });
        Task.Run(() =>
        {
            string? line;
            while ((line = _toBridge.Take()) != null)
            {
                if (!_handler(Protocol.FromLine<Request>(line), this))
                    break;
            }
            Close();
        });
    }

    public void Send(Response response) => _toClient.Add(Protocol.ToLine(response));

    public void Result<T>(Request request, T data) =>
        Send(new Response { Id = request.Id, Kind = MessageKind.Result, Data = Protocol.ToElement(data) });

    public void Error(Request request, ErrorCode code, string text = "") =>
        Send(new Response { Id = request.Id, Kind = MessageKind.Error, Code = code, Text = text });

    /// <summary>Cierra la salida del puente, como si hubiera terminado.</summary>
    public void Close() => _toClient.Complete();

    internal sealed class LineQueue
    {
        private readonly BlockingCollection<string> _lines = new();

        public void Add(string line)
        {
            if (!_lines.IsAddingCompleted)
                _lines.Add(line);
        }

        public void Complete() => _lines.CompleteAdding();

        /// <summary>La siguiente línea, o null si se ha cerrado.</summary>
        public string? Take() => _lines.TryTake(out var line, -1) ? line : null;
    }

    private sealed class QueueReader : TextReader
    {
        private readonly LineQueue _queue;

        public QueueReader(LineQueue queue)
        {
            _queue = queue;
        }

        public override string? ReadLine() => _queue.Take();

        public override Task<string?> ReadLineAsync() => Task.Run(ReadLine);
    }

    private sealed class QueueWriter : TextWriter
    {
        private readonly LineQueue _queue;
        private readonly StringBuilder _pending = new();

        public QueueWriter(LineQueue queue)
        {
            _queue = queue;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                _queue.Add(_pending.ToString());
                _pending.Clear();
            }
            else
            {
                _pending.Append(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (char c in value ?? "")
                Write(c);
        }

        public override Task WriteAsync(string? value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;

        protected override void Dispose(bool disposing)
        {
            _queue.Complete();
            base.Dispose(disposing);
        }
    }
}
