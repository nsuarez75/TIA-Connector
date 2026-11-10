using Siemens.Engineering;
using System;
using System.IO;
using TiaConnector.Contracts;

namespace TiaConnector.Core;

/// <summary>Fallo de un elemento con su código; lo lanzan las operaciones y lo recoge <see cref="BatchRunner"/>.</summary>
public sealed class ItemFailureException : Exception
{
    public ItemFailureException(FailureCode code, string detail = "")
        : base(detail.Length == 0 ? code.ToString() : detail)
    {
        Code = code;
        Detail = detail;
    }

    public FailureCode Code { get; }
    public string Detail { get; }
}

/// <summary>
/// La forma común de hacer cada elemento de una operación sobre varios: si falla, lo apunta y sigue; si TIA se ha
/// cerrado o ya no tiene el proyecto, para (seguir solo llenaría la lista con el mismo error) y apunta cuántos quedan.
/// </summary>
public sealed class BatchRunner
{
    private readonly Func<bool> _projectIsAlive;

    public BatchRunner(BatchResult result, Func<bool> projectIsAlive)
    {
        Result = result;
        _projectIsAlive = projectIsAlive;
    }

    public BatchResult Result { get; }

    /// <summary>Hace el trabajo de un elemento. Devuelve false si hay que parar.</summary>
    public bool Run(string item, int remainingAfterThis, Action work)
    {
        try
        {
            work();
            Result.Succeeded++;
            return true;
        }
        catch (Exception e)
        {
            Result.Failures.Add(ToFailure(item, e));
            if (!(e is EngineeringObjectDisposedException) || _projectIsAlive())
                return true;
            Result.Stopped = true;
            Result.Remaining = remainingAfterThis;
            return false;
        }
    }

    /// <summary>Apunta un fallo de un elemento que sí se hizo (por ejemplo, importado pero no compila).</summary>
    public void AddFailure(string item, FailureCode code, string detail = "") =>
        Result.Failures.Add(new ItemFailure { Item = item, Code = code, Detail = detail });

    public static ItemFailure ToFailure(string item, Exception e) => e switch
    {
        ItemFailureException failure => new ItemFailure { Item = item, Code = failure.Code, Detail = failure.Detail },
        FileNotFoundException => new ItemFailure { Item = item, Code = FailureCode.NotFound, Detail = e.Message },
        PathTooLongException => new ItemFailure { Item = item, Code = FailureCode.PathTooLong, Detail = e.Message },
        _ => new ItemFailure { Item = item, Code = FailureCode.Other, Detail = e.Message },
    };
}
