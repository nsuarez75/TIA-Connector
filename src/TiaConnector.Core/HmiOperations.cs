using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.RuntimeScripting;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.Hmi.Tag;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaConnector.Contracts;

namespace TiaConnector.Core;

/// <summary>Leer, exportar e importar las ramas de una HMI WinCC clásica. Solo en el hilo de Openness.</summary>
public sealed class HmiOperations
{
    private readonly ProjectAccess _project;
    private readonly IProgress<ProgressInfo> _progress;

    public HmiOperations(ProjectAccess project, IProgress<ProgressInfo> progress)
    {
        _project = project;
        _progress = progress;
    }

    // ------------------------------------------------------------------ Leer

    /// <summary>Todas las ramas de la HMI, avisando por cuál va.</summary>
    public HmiTree ReadHmi(HmiTarget hmi, int current, int total)
    {
        var tree = new HmiTree { Name = hmi.Name };
        tree.Branches.AddRange(ReadBranches(hmi, HmiRules.TreeOrder, branch =>
            _progress.Report(new ProgressInfo { Stage = ProgressStage.ReadingTree, Current = current, Total = total, Item = $"{hmi.Name}/{branch}" })));
        return tree;
    }

    /// <summary>Las ramas pedidas, en el orden del árbol. Las que el panel no tiene no salen.</summary>
    public List<HmiBranchTree> ReadBranches(ReadHmiBranchesArgs args) =>
        ReadBranches(_project.FindHmi(args.Hmi), args.Branches, null);

    private static List<HmiBranchTree> ReadBranches(HmiTarget hmi, IEnumerable<HmiBranch> branches, Action<HmiBranch>? beforeEach)
    {
        var wanted = new HashSet<HmiBranch>(branches);
        var read = new List<HmiBranchTree>();
        foreach (var branch in HmiRules.TreeOrder.Where(wanted.Contains))
        {
            beforeEach?.Invoke(branch);
            if (ReadBranch(hmi, branch) is { } root)
                read.Add(new HmiBranchTree { Branch = branch, Root = root });
        }
        return read;
    }

    private static HmiFolder? ReadBranch(HmiTarget hmi, HmiBranch branch) => branch switch
    {
        HmiBranch.Screens => ReadFolder<ScreenFolder>(hmi.ScreenFolder, f => f.Name, f => f.Folders, f => f.Screens.Select(e => e.Name)),
        HmiBranch.Templates => ReadFolder<ScreenTemplateFolder>(hmi.ScreenTemplateFolder, f => f.Name, f => f.Folders, f => f.ScreenTemplates.Select(e => e.Name)),
        HmiBranch.PopUps => ReadFolder<ScreenPopupFolder>(hmi.ScreenPopupFolder, f => f.Name, f => f.Folders, f => f.ScreenPopups.Select(e => e.Name)),
        HmiBranch.Tags => ReadFolder<TagFolder>(hmi.TagFolder, f => f.Name, f => f.Folders, f => f.TagTables.Select(e => e.Name)),
        HmiBranch.Scripts => ReadFolder<VBScriptFolder>(hmi.VBScriptFolder, f => f.Name, f => f.Folders, f => f.VBScripts.Select(e => e.Name)),
        HmiBranch.SlideIns => hmi.ScreenSlideinFolder == null ? null : List(hmi.ScreenSlideinFolder.ScreenSlideins.Select(e => e.SlideinType.ToString())),
        HmiBranch.Connections => List(hmi.Connections.Select(e => e.Name)),
        HmiBranch.Cycles => List(hmi.Cycles.Select(e => e.Name)),
        HmiBranch.TextLists => List(hmi.TextLists.Select(e => e.Name)),
        HmiBranch.GraphicLists => List(hmi.GraphicLists.Select(e => e.Name)),
        HmiBranch.GlobalElements => hmi.ScreenGlobalElements == null ? null : new HmiFolder(),
        HmiBranch.ScreenOverview => hmi.ScreenOverview == null ? null : new HmiFolder(),
        _ => throw new ArgumentOutOfRangeException(nameof(branch)),
    };

    // T es el tipo base de la carpeta: la raíz de la rama y sus subcarpetas son de tipos distintos (System/User).
    private static HmiFolder? ReadFolder<T>(T? folder, Func<T, string> name, Func<T, IEnumerable> subfolders, Func<T, IEnumerable<string>> items)
        where T : class
    {
        if (folder == null)
            return null;
        var read = new HmiFolder { Name = name(folder) };
        foreach (T sub in subfolders(folder))
            read.Folders.Add(ReadFolder(sub, name, subfolders, items)!);
        read.Items.AddRange(items(folder));
        return read;
    }

    private static HmiFolder List(IEnumerable<string> items)
    {
        var folder = new HmiFolder();
        folder.Items.AddRange(items);
        return folder;
    }

    // ------------------------------------------------------------------ Exportar

    /// <summary>
    /// Cada elemento en &lt;carpeta&gt;\[HMI\]&lt;rama&gt;\&lt;carpetas&gt;\&lt;nombre&gt;.xml. La subcarpeta de la HMI solo
    /// aparece si hay elementos de varias.
    /// </summary>
    public BatchResult Export(ExportHmiArgs args)
    {
        BlockOperations.RequireFolder(args.Folder);
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        bool severalHmis = args.Items.Select(i => i.Hmi).Distinct().Count() > 1;

        for (int i = 0; i < args.Items.Count; i++)
        {
            var item = args.Items[i];
            int index = i;
            _progress.Report(new ProgressInfo { Stage = ProgressStage.Exporting, Current = i + 1, Total = args.Items.Count, Item = item.Name });
            bool goOn = batch.Run(item.Name, args.Items.Count - i - 1, () =>
            {
                var parts = new List<string> { args.Folder };
                if (severalHmis)
                    parts.Add(FileRules.SafeName(item.Hmi));
                parts.Add(HmiRules.FolderName(item.Branch));
                parts.AddRange(item.Folders.Select(FileRules.SafeName));
                string folder = Path.Combine(parts.ToArray());
                string fileName = HmiRules.SingleItemFileName(item.Branch) ?? item.Name;
                string path = Path.Combine(folder, FileRules.SafeName(fileName) + ".xml");

                var export = Exporter(_project.FindHmi(item.Hmi), item) ?? throw new ItemFailureException(FailureCode.NotFound);
                Directory.CreateDirectory(folder);
                BlockOperations.ExportTo(path, export);
                result.Files.Add(new ExportedFile { Index = index, Item = item.Name, Path = path });
            });
            if (!goOn)
                break;
        }
        return result;
    }

    private static Action<FileInfo>? Exporter(HmiTarget hmi, HmiItemRef item)
    {
        var options = ExportOptions.WithDefaults;
        switch (item.Branch)
        {
            case HmiBranch.Screens:
                var screen = ScreenFolder(hmi, item.Folders, false)?.Screens.Find(item.Name);
                return screen == null ? null : f => screen.Export(f, options);
            case HmiBranch.Templates:
                var template = TemplateFolder(hmi, item.Folders, false)?.ScreenTemplates.Find(item.Name);
                return template == null ? null : f => template.Export(f, options);
            case HmiBranch.PopUps:
                var popup = PopUpFolder(hmi, item.Folders, false)?.ScreenPopups.Find(item.Name);
                return popup == null ? null : f => popup.Export(f, options);
            case HmiBranch.Tags:
                var table = TagFolder(hmi, item.Folders, false)?.TagTables.Find(item.Name);
                return table == null ? null : f => table.Export(f, options);
            case HmiBranch.Scripts:
                var script = ScriptFolder(hmi, item.Folders, false)?.VBScripts.Find(item.Name);
                return script == null ? null : f => script.Export(f, options);
            case HmiBranch.SlideIns:
                var slideIn = hmi.ScreenSlideinFolder?.ScreenSlideins.FirstOrDefault(e => e.SlideinType.ToString() == item.Name);
                return slideIn == null ? null : f => slideIn.Export(f, options);
            case HmiBranch.Connections:
                var connection = hmi.Connections.Find(item.Name);
                return connection == null ? null : f => connection.Export(f, options);
            case HmiBranch.Cycles:
                var cycle = hmi.Cycles.Find(item.Name);
                return cycle == null ? null : f => cycle.Export(f, options);
            case HmiBranch.TextLists:
                var textList = hmi.TextLists.Find(item.Name);
                return textList == null ? null : f => textList.Export(f, options);
            case HmiBranch.GraphicLists:
                var graphicList = hmi.GraphicLists.Find(item.Name);
                return graphicList == null ? null : f => graphicList.Export(f, options);
            case HmiBranch.GlobalElements:
                var globals = hmi.ScreenGlobalElements;
                return globals == null ? null : f => globals.Export(f, options);
            case HmiBranch.ScreenOverview:
                var overview = hmi.ScreenOverview;
                return overview == null ? null : f => overview.Export(f, options);
            default:
                throw new ArgumentOutOfRangeException(nameof(item));
        }
    }

    // ------------------------------------------------------------------ Importar

    /// <summary>
    /// Importa los XML en la HMI, cada uno en su rama y carpetas (las que faltan se crean), en el orden de las ramas:
    /// primero lo que usan las demás. Lo que ya existe se sobrescribe, salvo la tabla de variables estándar.
    /// </summary>
    public BatchResult Import(ImportHmiArgs args)
    {
        var hmi = _project.FindHmi(args.Hmi);
        var files = args.Files.OrderBy(f => f.Branch).ToList();
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            string item = Path.GetFileName(file.Path);
            _progress.Report(new ProgressInfo { Stage = ProgressStage.Importing, Current = i + 1, Total = files.Count, Item = item });
            if (!batch.Run(item, files.Count - i - 1, () => ImportFile(hmi, file)))
                break;
        }
        return result;
    }

    private static void ImportFile(HmiTarget hmi, HmiImportFile file)
    {
        BlockOperations.RequireFile(file.Path);
        var xml = new FileInfo(file.Path);
        var options = ImportOptions.Override;
        IReadOnlyList<string> folders = HmiRules.HasFolders(file.Branch) ? file.Folders : Array.Empty<string>();
        switch (file.Branch)
        {
            case HmiBranch.Screens: ScreenFolder(hmi, folders, true)!.Screens.Import(xml, options); break;
            case HmiBranch.Templates: TemplateFolder(hmi, folders, true)!.ScreenTemplates.Import(xml, options); break;
            case HmiBranch.PopUps: PopUpFolder(hmi, folders, true)!.ScreenPopups.Import(xml, options); break;
            case HmiBranch.Tags:
                // Sobrescribir la tabla de variables estándar ha llegado a cerrar TIA: nunca se toca.
                string? table = FileRules.ReadObjectName(file.Path);
                if (table != null && table.Equals(hmi.TagFolder?.DefaultTagTable?.Name, StringComparison.OrdinalIgnoreCase))
                    throw new ItemFailureException(FailureCode.DefaultTagTable);
                TagFolder(hmi, folders, true)!.TagTables.Import(xml, options);
                break;
            case HmiBranch.Scripts: ScriptFolder(hmi, folders, true)!.VBScripts.Import(xml, options); break;
            case HmiBranch.SlideIns: Required(hmi.ScreenSlideinFolder).ScreenSlideins.Import(xml, options); break;
            case HmiBranch.Connections: hmi.Connections.Import(xml, options); break;
            case HmiBranch.Cycles: hmi.Cycles.Import(xml, options); break;
            case HmiBranch.TextLists: hmi.TextLists.Import(xml, options); break;
            case HmiBranch.GraphicLists: hmi.GraphicLists.Import(xml, options); break;
            case HmiBranch.GlobalElements: hmi.ImportScreenGlobalElements(xml, options); break;
            case HmiBranch.ScreenOverview: hmi.ImportScreenOverview(xml, options); break;
            default: throw new ArgumentOutOfRangeException(nameof(file));
        }
    }

    // ------------------------------------------------------------------ Carpetas

    private static T Required<T>(T? folder) where T : class =>
        folder ?? throw new ItemFailureException(FailureCode.Unsupported);

    private static ScreenFolder? ScreenFolder(HmiTarget hmi, IReadOnlyList<string> folders, bool create) =>
        Navigate<ScreenFolder>(Required(hmi.ScreenFolder), folders, create, (f, n) => f.Folders.Find(n), (f, n) => f.Folders.Create(n));

    private static ScreenTemplateFolder? TemplateFolder(HmiTarget hmi, IReadOnlyList<string> folders, bool create) =>
        Navigate<ScreenTemplateFolder>(Required(hmi.ScreenTemplateFolder), folders, create, (f, n) => f.Folders.Find(n), (f, n) => f.Folders.Create(n));

    private static ScreenPopupFolder? PopUpFolder(HmiTarget hmi, IReadOnlyList<string> folders, bool create) =>
        Navigate<ScreenPopupFolder>(Required(hmi.ScreenPopupFolder), folders, create, (f, n) => f.Folders.Find(n), (f, n) => f.Folders.Create(n));

    private static TagFolder? TagFolder(HmiTarget hmi, IReadOnlyList<string> folders, bool create) =>
        Navigate<TagFolder>(Required(hmi.TagFolder), folders, create, (f, n) => f.Folders.Find(n), (f, n) => f.Folders.Create(n));

    private static VBScriptFolder? ScriptFolder(HmiTarget hmi, IReadOnlyList<string> folders, bool create) =>
        Navigate<VBScriptFolder>(Required(hmi.VBScriptFolder), folders, create, (f, n) => f.Folders.Find(n), (f, n) => f.Folders.Create(n));

    /// <summary>Baja por las carpetas; si falta una, la crea o devuelve null según <paramref name="create"/>.</summary>
    private static T? Navigate<T>(T root, IReadOnlyList<string> folders, bool create, Func<T, string, T?> find, Func<T, string, T> createFolder)
        where T : class
    {
        var folder = root;
        foreach (string name in folders)
        {
            var next = find(folder, name);
            if (next == null)
            {
                if (!create)
                    return null;
                next = createFolder(folder, name);
            }
            folder = next;
        }
        return folder;
    }
}
