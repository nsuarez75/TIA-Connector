using System.Collections.Generic;

namespace TiaConnector.Contracts;

/// <summary>Nombres de las operaciones del puente (campo <c>op</c> de la petición).</summary>
public static class Operations
{
    public const string Version = "version";
    public const string OpenProjects = "openProjects";
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string ReadTree = "readTree";
    public const string ReadPlc = "readPlc";
    public const string ReadHmiBranches = "readHmiBranches";
    public const string ExportBlocks = "exportBlocks";
    public const string ImportBlocks = "importBlocks";
    public const string Compile = "compile";
    public const string ExportProgram = "exportProgram";
    public const string ImportTypes = "importTypes";
    public const string ImportTagTables = "importTagTables";
    public const string ExportHmi = "exportHmi";
    public const string ImportHmi = "importHmi";
}

/// <summary>Qué hacer al conectar si el proyecto no está abierto en ningún TIA.</summary>
public enum OpenMode
{
    /// <summary>Falla con <see cref="ErrorCode.ProjectNotOpen"/>.</summary>
    Fail,

    /// <summary>Lo abre en un TIA sin ventana del puente, que se cierra al desconectar o al cerrar el puente.</summary>
    WithoutWindow,

    /// <summary>
    /// Lo abre en un TIA normal, como un doble clic en el proyecto. Ese TIA no depende del puente y sigue abierto
    /// para que el usuario revise y guarde.
    /// </summary>
    WithWindow,
}

public sealed class ConnectArgs
{
    public string ProjectPath { get; set; } = "";
    public OpenMode IfNotOpen { get; set; } = OpenMode.Fail;
}

public sealed class ReadTreeArgs
{
    public List<string> Plcs { get; set; } = new();
    public List<string> Hmis { get; set; } = new();
}

public sealed class ReadPlcArgs
{
    public string Plc { get; set; } = "";
}

public sealed class ReadHmiBranchesArgs
{
    public string Hmi { get; set; } = "";
    public List<HmiBranch> Branches { get; set; } = new();
}

/// <summary>Cómo quedan los archivos de una exportación de bloques dentro de la carpeta.</summary>
public enum ExportLayout
{
    /// <summary>Con las carpetas de TIA. Si hay bloques de varios PLC, cada PLC en su subcarpeta.</summary>
    Folders,

    /// <summary>Todos juntos; si se repite un nombre, el archivo se numera (Nombre_2.xml).</summary>
    Flat,

    /// <summary>Una subcarpeta por bloque (0, 1, 2…, su posición en la petición): nunca chocan nombres.</summary>
    PerBlock,
}

public sealed class ExportBlocksArgs
{
    public List<BlockRef> Blocks { get; set; } = new();
    public string Folder { get; set; } = "";

    /// <summary>Compila antes los bloques sin compilar. Si es false, esos bloques fallan con NotCompiled.</summary>
    public bool CompileIfNeeded { get; set; } = true;

    public ExportLayout Layout { get; set; } = ExportLayout.Folders;
}

/// <summary>Un XML (o fuente, en los tipos) a importar en un PLC.</summary>
public sealed class ImportFile
{
    public string Path { get; set; } = "";
    public string Plc { get; set; } = "";

    /// <summary>Carpetas de destino desde la raíz; las que faltan se crean.</summary>
    public List<string> Folders { get; set; } = new();

    /// <summary>
    /// Solo bloques: si el PLC ya tiene un bloque con el nombre del XML, se importa en su carpeta (sobrescribiéndolo)
    /// y <see cref="Folders"/> solo se usa si no existe.
    /// </summary>
    public bool UseExistingFolder { get; set; }
}

public sealed class ImportBlocksArgs
{
    public List<ImportFile> Files { get; set; } = new();

    /// <summary>
    /// Compila cada bloque después de importarlo. Si no compila, queda importado, cuenta como hecho y además sale
    /// un fallo DoesNotCompile.
    /// </summary>
    public bool CompileAfter { get; set; }
}

public enum CompileScope
{
    Blocks,
    Plc,
    Project,
}

public sealed class CompileArgs
{
    public CompileScope Scope { get; set; } = CompileScope.Project;
    public List<BlockRef> Blocks { get; set; } = new();
    public string Plc { get; set; } = "";
}

public sealed class ExportProgramArgs
{
    public string Plc { get; set; } = "";
    public string Folder { get; set; } = "";
}

public sealed class ImportFilesArgs
{
    public List<ImportFile> Files { get; set; } = new();
}

public sealed class ExportHmiArgs
{
    public List<HmiItemRef> Items { get; set; } = new();
    public string Folder { get; set; } = "";
}

/// <summary>Un XML a importar en una HMI, con su rama y las carpetas donde debe quedar dentro de ella.</summary>
public sealed class HmiImportFile
{
    public string Path { get; set; } = "";
    public HmiBranch Branch { get; set; }
    public List<string> Folders { get; set; } = new();
}

public sealed class ImportHmiArgs
{
    public string Hmi { get; set; } = "";

    /// <summary>Se importan en el orden de las ramas (<see cref="HmiBranch"/>), no en el de la lista.</summary>
    public List<HmiImportFile> Files { get; set; } = new();
}

public sealed class VersionInfo
{
    public string Bridge { get; set; } = "";
    public int Protocol { get; set; }
    public string Tia { get; set; } = "";
}
