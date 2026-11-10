using System.Collections.Generic;
using System.Linq;

namespace TiaConnector.Contracts;

// Copias simples de lo que se lee de TIA. Viajan por el protocolo en JSON: nunca llevan objetos de Openness.

/// <summary>Un TIA Portal en marcha con un proyecto abierto.</summary>
public sealed class OpenProject
{
    public int ProcessId { get; set; }
    public string ProjectPath { get; set; } = "";
}

public enum DeviceKind
{
    Plc,

    /// <summary>HMI WinCC clásica (Basic, Comfort, Advanced). Las Unified no salen.</summary>
    Hmi,
}

/// <summary>Un PLC o una HMI del proyecto, sin lo que tiene dentro.</summary>
public sealed class DeviceInfo
{
    public string Name { get; set; } = "";
    public DeviceKind Kind { get; set; }
}

/// <summary>Lo que se sabe del proyecto al conectar.</summary>
public sealed class ProjectInfo
{
    public string Name { get; set; } = "";
    public string ProjectPath { get; set; } = "";

    /// <summary>Carpeta del proyecto (la del .ap19).</summary>
    public string ProjectFolder { get; set; } = "";

    /// <summary>Idioma de edición (en-US…); con él se leen los textos multilingües del programa.</summary>
    public string EditingLanguage { get; set; } = "";

    /// <summary>True si el puente lo abrió en un TIA propio porque no estaba abierto en ninguno.</summary>
    public bool OpenedByBridge { get; set; }

    public List<DeviceInfo> Devices { get; set; } = new();
}

/// <summary>Árbol de bloques de un PLC.</summary>
public sealed class PlcTree
{
    public string Name { get; set; } = "";
    public BlockFolder Root { get; set; } = new();
}

public sealed class BlockFolder
{
    public string Name { get; set; } = "";
    public List<BlockFolder> Folders { get; set; } = new();
    public List<BlockInfo> Blocks { get; set; } = new();
}

public sealed class BlockInfo
{
    public string Name { get; set; } = "";

    /// <summary>OB, FB, FC o DB (todos los tipos de DB salen como «DB»).</summary>
    public string Type { get; set; } = "";

    public int Number { get; set; }

    /// <summary>Protegido con contraseña (know-how): no se puede exportar.</summary>
    public bool IsProtected { get; set; }
}

/// <summary>Dónde está un bloque: PLC, carpetas desde la raíz de bloques y nombre. Nunca solo por nombre.</summary>
public sealed class BlockRef
{
    public string Plc { get; set; } = "";
    public List<string> Folders { get; set; } = new();
    public string Name { get; set; } = "";
    public bool IsProtected { get; set; }

    public override string ToString() => string.Join("/", new[] { Plc }.Concat(Folders).Concat(new[] { Name }));
}

/// <summary>
/// Rama de una HMI WinCC clásica que se puede exportar e importar. El orden del enum es el orden de importación:
/// primero lo que usan las demás (las variables usan conexiones y ciclos; las pantallas, variables, listas, scripts y
/// plantillas; la vista general, pantallas).
/// </summary>
public enum HmiBranch
{
    Connections,
    Cycles,
    TextLists,
    GraphicLists,
    Tags,
    Scripts,
    Templates,
    GlobalElements,
    PopUps,
    SlideIns,
    Screens,
    ScreenOverview,
}

public sealed class HmiTree
{
    public string Name { get; set; } = "";
    public List<HmiBranchTree> Branches { get; set; } = new();
}

/// <summary>Una rama de la HMI. En las ramas de un solo elemento (elementos globales, vista general) Root está vacía.</summary>
public sealed class HmiBranchTree
{
    public HmiBranch Branch { get; set; }
    public HmiFolder Root { get; set; } = new();
}

public sealed class HmiFolder
{
    public string Name { get; set; } = "";
    public List<HmiFolder> Folders { get; set; } = new();
    public List<string> Items { get; set; } = new();
}

/// <summary>Dónde está un elemento de HMI: HMI, rama, carpetas dentro de la rama y nombre.</summary>
public sealed class HmiItemRef
{
    public string Hmi { get; set; } = "";
    public HmiBranch Branch { get; set; }
    public List<string> Folders { get; set; } = new();
    public string Name { get; set; } = "";
}

/// <summary>Árbol del proyecto: los PLC y HMI pedidos.</summary>
public sealed class ProjectTree
{
    public List<PlcTree> Plcs { get; set; } = new();
    public List<HmiTree> Hmis { get; set; } = new();
}
