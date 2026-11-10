using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TiaConnector.Contracts;

/// <summary>
/// Protocolo entre cliente y puente: una línea JSON por mensaje (UTF-8 sin BOM). El cliente manda peticiones; el
/// puente las atiende de una en una, en orden, y contesta con avisos de progreso y una sola respuesta final.
/// </summary>
public static class Protocol
{
    /// <summary>Sube cuando cambia algo que un cliente o un puente anteriores no entenderían.</summary>
    public const int Version = 1;

    public static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Una línea del protocolo: JSON sin saltos de línea.</summary>
    public static string ToLine<T>(T message) => JsonSerializer.Serialize(message, JsonOptions);

    public static T FromLine<T>(string line) =>
        JsonSerializer.Deserialize<T>(line, JsonOptions) ?? throw new JsonException("Mensaje vacío.");

    public static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    public static T FromElement<T>(JsonElement? element) =>
        element is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } value
            ? JsonSerializer.Deserialize<T>(value.GetRawText(), JsonOptions) ?? throw new JsonException("Datos vacíos.")
            : throw new JsonException("Faltan los datos.");
}

/// <summary>Petición del cliente al puente.</summary>
public sealed class Request
{
    public long Id { get; set; }
    public string Op { get; set; } = "";
    public JsonElement? Args { get; set; }
}

public enum MessageKind
{
    /// <summary>Primera línea del puente al arrancar, sin id.</summary>
    Ready,

    Progress,
    Warning,

    /// <summary>Respuesta final correcta.</summary>
    Result,

    /// <summary>Respuesta final con error: la operación no se hizo (o se hizo a medias, según la operación).</summary>
    Error,
}

/// <summary>Mensaje del puente al cliente. Todos llevan el id de su petición, salvo Ready.</summary>
public sealed class Response
{
    public long? Id { get; set; }
    public MessageKind Kind { get; set; }

    public ProgressInfo? Progress { get; set; }

    /// <summary>Aviso o mensaje de error, en inglés y sin traducir: la app se guía por <see cref="Code"/>.</summary>
    public string? Text { get; set; }

    public ErrorCode? Code { get; set; }
    public JsonElement? Data { get; set; }

    /// <summary>Solo en Ready: versión del protocolo del puente.</summary>
    public int? Protocol { get; set; }

    /// <summary>Solo en Ready: versión del puente.</summary>
    public string? BridgeVersion { get; set; }
}

public enum ProgressStage
{
    Connecting,
    OpeningProject,
    ReadingTree,
    Exporting,
    Importing,
    Compiling,
}

/// <summary>Por dónde va una operación. La app compone el texto en su idioma.</summary>
public sealed class ProgressInfo
{
    public ProgressStage Stage { get; set; }

    /// <summary>Elemento en curso (1…Total); 0 si la etapa no se cuenta.</summary>
    public int Current { get; set; }

    public int Total { get; set; }

    /// <summary>Bloque, elemento, dispositivo o rama en curso.</summary>
    public string Item { get; set; } = "";
}

/// <summary>Errores de una petición entera. La app traduce el código a su idioma.</summary>
public enum ErrorCode
{
    /// <summary>No está instalado TIA Portal V19 con Openness.</summary>
    NoOpenness,

    /// <summary>El usuario no aceptó el aviso de TIA o no está en el grupo «Siemens TIA Openness».</summary>
    NoPermission,

    /// <summary>Hace falta conectar con un proyecto antes.</summary>
    NotConnected,

    /// <summary>El proyecto no está abierto en ningún TIA (o ya no lo está).</summary>
    ProjectNotOpen,

    /// <summary>TIA se cerró durante la operación.</summary>
    TiaClosed,

    /// <summary>No existe el PLC, la HMI, la carpeta o el proyecto pedidos.</summary>
    NotFound,

    Protected,
    DoesNotCompile,
    PathTooLong,

    /// <summary>Petición mal formada o con datos que faltan.</summary>
    InvalidRequest,

    UnknownOperation,

    /// <summary>El puente terminó o no arrancó (solo lo produce el cliente).</summary>
    BridgeExited,

    /// <summary>Cliente y puente hablan versiones distintas del protocolo.</summary>
    ProtocolMismatch,

    /// <summary>Fallo no previsto; el detalle va en el texto y en el registro del puente.</summary>
    Internal,
}

/// <summary>Error de una petición, con su código. Lo lanzan el núcleo (en el puente) y el cliente.</summary>
public sealed class TiaConnectorException : Exception
{
    public TiaConnectorException(ErrorCode code, string message, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    public ErrorCode Code { get; }
}
