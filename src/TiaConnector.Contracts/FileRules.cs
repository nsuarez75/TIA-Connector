using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace TiaConnector.Contracts;

/// <summary>Reglas de nombres y rutas de los XML de TIA. No dependen de TIA: las usan el puente y las apps.</summary>
public static class FileRules
{
    /// <summary>Cambia por '_' los caracteres que Windows no admite en nombres de archivo o carpeta.</summary>
    public static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "_" : clean;
    }

    /// <summary>
    /// Si la ruta del archivo pasa del límite de Windows (y de TIA): 259 caracteres el archivo, 247 su carpeta.
    /// Las carpetas de una HMI suelen ser profundas y con nombres largos.
    /// </summary>
    public static bool IsPathTooLong(string filePath)
    {
        int lastSlash = filePath.LastIndexOfAny(new[] { '\\', '/' });
        return filePath.Length >= 260 || lastSlash >= 248;
    }

    /// <summary>
    /// Ruta &lt;carpeta&gt;\&lt;nombre&gt;.xml que no existe todavía: si ya hay un archivo con ese nombre (dos objetos
    /// iguales en carpetas distintas de TIA), se numera: Nombre_2.xml, Nombre_3.xml…
    /// </summary>
    public static string FreeXmlPath(string folder, string name)
    {
        string safe = SafeName(name);
        string path = Path.Combine(folder, safe + ".xml");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{safe}_{i}.xml");
        return path;
    }

    /// <summary>Carpetas entre la carpeta base y el archivo, para recrearlas en TIA. Vacío si el archivo está fuera.</summary>
    public static List<string> RelativeFolders(string baseFolder, string filePath)
    {
        string fullBase = Path.GetFullPath(baseFolder).TrimEnd('\\', '/') + "\\";
        string fileFolder = Path.GetDirectoryName(Path.GetFullPath(filePath)) + "\\";
        if (!fileFolder.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            return new List<string>();
        return fileFolder.Substring(fullBase.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// Nombre del objeto que contiene un XML exportado de TIA (su primer AttributeList/Name), o null si no lo tiene o
    /// no se puede leer. Lee solo el principio del archivo.
    /// </summary>
    public static string? ReadObjectName(string xmlPath)
    {
        try
        {
            using var reader = XmlReader.Create(xmlPath, SafeReaderSettings());
            bool inAttributes = false;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.LocalName == "AttributeList")
                    inAttributes = true;
                else if (inAttributes && reader.LocalName == "Name")
                    return reader.ReadElementContentAsString().Trim();
            }
        }
        catch (Exception e) when (e is XmlException || e is IOException || e is UnauthorizedAccessException)
        {
            // Sin nombre: quien llama decide qué hacer.
        }
        return null;
    }

    internal static XmlReaderSettings SafeReaderSettings() =>
        new() { DtdProcessing = DtdProcessing.Prohibit, IgnoreComments = true };
}

/// <summary>Reglas de las ramas de una HMI WinCC clásica: nombres de carpeta, carpetas propias y tipo de cada XML.</summary>
public static class HmiRules
{
    /// <summary>Orden en que se ven las ramas en el árbol de TIA.</summary>
    public static readonly IReadOnlyList<HmiBranch> TreeOrder = new[]
    {
        HmiBranch.Screens, HmiBranch.Templates, HmiBranch.PopUps, HmiBranch.SlideIns, HmiBranch.GlobalElements,
        HmiBranch.ScreenOverview, HmiBranch.Tags, HmiBranch.Connections, HmiBranch.Cycles, HmiBranch.TextLists,
        HmiBranch.GraphicLists, HmiBranch.Scripts,
    };

    /// <summary>
    /// Carpeta de la rama al exportar. Se mantienen los nombres en castellano que ya usa HerramientasSICAR para que sus
    /// exportaciones sigan valiendo.
    /// </summary>
    public static string FolderName(HmiBranch branch) => branch switch
    {
        HmiBranch.Screens => "Pantallas",
        HmiBranch.Templates => "Plantillas",
        HmiBranch.PopUps => "Pop-ups",
        HmiBranch.SlideIns => "Slide-ins",
        HmiBranch.GlobalElements => "Elementos globales",
        HmiBranch.ScreenOverview => "Vista general de pantallas",
        HmiBranch.Tags => "Variables HMI",
        HmiBranch.Connections => "Conexiones",
        HmiBranch.Cycles => "Ciclos",
        HmiBranch.TextLists => "Listas de textos",
        HmiBranch.GraphicLists => "Listas de gráficos",
        HmiBranch.Scripts => "Scripts VB",
        _ => throw new ArgumentOutOfRangeException(nameof(branch)),
    };

    /// <summary>Archivo de las ramas que son un único elemento (no tienen nombre propio en TIA).</summary>
    public static string? SingleItemFileName(HmiBranch branch) => branch switch
    {
        HmiBranch.GlobalElements => "ElementosGlobales",
        HmiBranch.ScreenOverview => "VistaGeneral",
        _ => null,
    };

    /// <summary>Ramas que en TIA admiten carpetas propias.</summary>
    public static bool HasFolders(HmiBranch branch) =>
        branch is HmiBranch.Screens or HmiBranch.Templates or HmiBranch.PopUps or HmiBranch.Tags or HmiBranch.Scripts;

    /// <summary>Ramas que son un único elemento (no una lista).</summary>
    public static bool IsSingleItem(HmiBranch branch) => branch is HmiBranch.GlobalElements or HmiBranch.ScreenOverview;

    /// <summary>
    /// Rama del elemento de HMI que contiene el XML, o null si no es de HMI. Openness nombra el elemento
    /// "Hmi.&lt;espacio&gt;.&lt;tipo&gt;"; se mira solo el tipo. Lee solo el principio del archivo.
    /// </summary>
    public static HmiBranch? DetectBranch(string xmlPath)
    {
        try
        {
            using var reader = XmlReader.Create(xmlPath, FileRules.SafeReaderSettings());
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;
                if (reader.LocalName.StartsWith("SW.", StringComparison.Ordinal))
                    return null;
                if (!reader.LocalName.StartsWith("Hmi.", StringComparison.Ordinal))
                    continue;
                return reader.LocalName.Substring(reader.LocalName.LastIndexOf('.') + 1) switch
                {
                    "Screen" => HmiBranch.Screens,
                    "ScreenTemplate" => HmiBranch.Templates,
                    "ScreenPopup" => HmiBranch.PopUps,
                    "ScreenSlidein" => HmiBranch.SlideIns,
                    "ScreenGlobalElements" => HmiBranch.GlobalElements,
                    "ScreenOverview" => HmiBranch.ScreenOverview,
                    "TagTable" => HmiBranch.Tags,
                    "Connection" => HmiBranch.Connections,
                    "Cycle" => HmiBranch.Cycles,
                    "TextList" => HmiBranch.TextLists,
                    "GraphicList" => HmiBranch.GraphicLists,
                    "VBScript" => HmiBranch.Scripts,
                    _ => null,
                };
            }
        }
        catch (Exception e) when (e is XmlException || e is IOException || e is UnauthorizedAccessException)
        {
            // No es un XML de HMI legible.
        }
        return null;
    }

    /// <summary>Carpetas que quedan después de la carpeta con el nombre de la rama; sin ella, todas.</summary>
    public static List<string> FoldersInsideBranch(HmiBranch branch, IReadOnlyList<string> folders)
    {
        if (!HasFolders(branch))
            return new List<string>();
        int index = folders.ToList().FindIndex(f => f.Equals(FolderName(branch), StringComparison.OrdinalIgnoreCase));
        return (index < 0 ? folders : folders.Skip(index + 1)).ToList();
    }
}

/// <summary>Carpeta en %TEMP%\TIA-Connector que se borra al terminar, aunque algo falle.</summary>
public sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TIA-Connector", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            // Si Windows la tiene bloqueada, se queda en %TEMP%.
        }
    }
}
