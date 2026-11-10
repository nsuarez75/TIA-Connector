using System.Collections.Generic;

namespace TiaConnector.Contracts;

/// <summary>Por qué falló un elemento de una operación sobre varios. La app traduce el código a su idioma.</summary>
public enum FailureCode
{
    /// <summary>Protegido con contraseña (know-how): Openness no lo exporta.</summary>
    Protected,

    /// <summary>Sin compilar y la operación no compila (por ejemplo, exportar el programa entero).</summary>
    NotCompiled,

    /// <summary>No compila. En una importación, el bloque queda importado igualmente.</summary>
    DoesNotCompile,

    /// <summary>Ya no existe en el proyecto (o el archivo a importar no existe).</summary>
    NotFound,

    /// <summary>La ruta del archivo pasa del límite de Windows.</summary>
    PathTooLong,

    /// <summary>Es la tabla de variables estándar de una HMI: sobrescribirla ha llegado a cerrar TIA.</summary>
    DefaultTagTable,

    /// <summary>El dispositivo no admite ese tipo de elemento.</summary>
    Unsupported,

    /// <summary>Otro fallo de TIA; el texto de TIA va en <see cref="ItemFailure.Detail"/>.</summary>
    Other,
}

/// <summary>Un elemento que no se pudo hacer.</summary>
public sealed class ItemFailure
{
    /// <summary>Nombre del bloque, elemento o archivo.</summary>
    public string Item { get; set; } = "";

    public FailureCode Code { get; set; }

    /// <summary>Texto de TIA o detalle técnico, sin traducir. Puede estar vacío.</summary>
    public string Detail { get; set; } = "";
}

/// <summary>Un archivo escrito por una exportación.</summary>
public sealed class ExportedFile
{
    /// <summary>Posición del elemento en la petición, para saber de cuál es.</summary>
    public int Index { get; set; }

    public string Item { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>
/// Resultado común de toda operación sobre varios elementos: los que salieron bien y los fallos. Si TIA se cierra a
/// mitad, la operación para: <see cref="Stopped"/> es true y <see cref="Remaining"/> dice cuántos quedaron sin hacer.
/// </summary>
public sealed class BatchResult
{
    public int Succeeded { get; set; }
    public List<ItemFailure> Failures { get; set; } = new();
    public List<ExportedFile> Files { get; set; } = new();
    public bool Stopped { get; set; }
    public int Remaining { get; set; }
}

public enum CompileState
{
    Success,
    Warning,
    Error,
}

public sealed class CompileMessage
{
    public CompileState State { get; set; }
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class CompileResult
{
    public CompileState State { get; set; }
    public int Errors { get; set; }
    public int Warnings { get; set; }

    /// <summary>Solo los mensajes de error y aviso, sin los nodos intermedios del árbol de TIA.</summary>
    public List<CompileMessage> Messages { get; set; } = new();
}
