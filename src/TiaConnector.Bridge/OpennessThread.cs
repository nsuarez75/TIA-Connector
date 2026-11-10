using System;
using System.Collections.Concurrent;
using System.Threading;

namespace TiaConnector.Bridge;

/// <summary>
/// Ejecuta todo el trabajo con Openness en un único hilo propio (STA), uno detrás de otro: Openness no admite llamadas
/// simultáneas. La lectura de la entrada estándar sigue en el hilo principal.
/// </summary>
internal sealed class OpennessThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public OpennessThread()
    {
        _thread = new Thread(Loop) { IsBackground = false, Name = "Openness" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public void Enqueue(Action work) => _queue.Add(work);

    /// <summary>No admite más trabajo y espera a que termine el que queda.</summary>
    public void CompleteAndWait()
    {
        _queue.CompleteAdding();
        _thread.Join();
    }

    private void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
            work();
    }

    public void Dispose() => _queue.Dispose();
}
