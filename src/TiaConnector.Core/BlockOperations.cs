using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaConnector.Contracts;

namespace TiaConnector.Core;

/// <summary>
/// Todo lo que se hace con el programa de un PLC: leer el árbol, exportar, importar y compilar. Un bloque se identifica
/// siempre por PLC, carpetas y nombre (<see cref="BlockRef"/>). Solo en el hilo de Openness.
/// </summary>
public sealed class BlockOperations
{
    private static readonly string[] SourceExtensions = { ".udt", ".scl", ".db", ".awl" };

    private readonly ProjectAccess _project;
    private readonly IProgress<ProgressInfo> _progress;

    public BlockOperations(ProjectAccess project, IProgress<ProgressInfo> progress)
    {
        _project = project;
        _progress = progress;
    }

    // ------------------------------------------------------------------ Leer

    public PlcTree ReadPlc(string plc) => ReadPlc(_project.FindPlc(plc));

    public static PlcTree ReadPlc(PlcSoftware plc) => new() { Name = plc.Name, Root = ReadFolder(plc.BlockGroup) };

    private static BlockFolder ReadFolder(PlcBlockGroup group)
    {
        var folder = new BlockFolder { Name = group.Name };
        foreach (PlcBlockGroup sub in group.Groups)
            folder.Folders.Add(ReadFolder(sub));
        foreach (PlcBlock block in group.Blocks)
        {
            folder.Blocks.Add(new BlockInfo
            {
                Name = block.Name,
                Type = BlockType(block),
                Number = block.Number,
                IsProtected = block.IsKnowHowProtected,
            });
        }
        return folder;
    }

    // InstanceDB, GlobalDB, ArrayDB… salen todos como «DB».
    private static string BlockType(PlcBlock block)
    {
        string type = block.GetType().Name;
        return type.EndsWith("DB", StringComparison.Ordinal) ? "DB" : type;
    }

    private PlcBlock FindBlock(BlockRef block) =>
        _project.FindBlockGroup(block.Plc, block.Folders, create: false)?.Blocks.Find(block.Name)
        ?? throw new ItemFailureException(FailureCode.NotFound);

    // ------------------------------------------------------------------ Exportar

    public BatchResult ExportBlocks(ExportBlocksArgs args)
    {
        RequireFolder(args.Folder);
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        bool severalPlcs = args.Blocks.Select(b => b.Plc).Distinct().Count() > 1;

        for (int i = 0; i < args.Blocks.Count; i++)
        {
            var block = args.Blocks[i];
            int index = i;
            Report(ProgressStage.Exporting, i + 1, args.Blocks.Count, block.Name);
            bool goOn = batch.Run(block.Name, args.Blocks.Count - i - 1, () =>
            {
                string folder = args.Layout switch
                {
                    ExportLayout.PerBlock => Path.Combine(args.Folder, index.ToString()),
                    ExportLayout.Flat => args.Folder,
                    _ => Path.Combine(new[] { args.Folder }
                        .Concat(severalPlcs ? new[] { FileRules.SafeName(block.Plc) } : Array.Empty<string>())
                        .Concat(block.Folders.Select(FileRules.SafeName)).ToArray()),
                };
                string path = ExportBlock(FindBlock(block), folder, args.CompileIfNeeded, numberIfExists: args.Layout == ExportLayout.Flat);
                result.Files.Add(new ExportedFile { Index = index, Item = block.Name, Path = path });
            });
            if (!goOn)
                break;
        }
        return result;
    }

    /// <summary>
    /// La única forma de exportar un bloque. Los protegidos no se pueden exportar. Uno sin compilar se compila antes
    /// si <paramref name="compileIfNeeded"/>; si no, falla con NotCompiled (así exportar no cambia nada del proyecto).
    /// </summary>
    private static string ExportBlock(PlcBlock block, string folder, bool compileIfNeeded, bool numberIfExists)
    {
        if (block.IsKnowHowProtected)
            throw new ItemFailureException(FailureCode.Protected);
        if (!block.IsConsistent)
        {
            if (!compileIfNeeded)
                throw new ItemFailureException(FailureCode.NotCompiled);
            if (!CompileBlock(block))
                throw new ItemFailureException(FailureCode.DoesNotCompile);
        }

        Directory.CreateDirectory(folder);
        string path = numberIfExists ? FileRules.FreeXmlPath(folder, block.Name) : Path.Combine(folder, FileRules.SafeName(block.Name) + ".xml");
        ExportTo(path, file => block.Export(file, ExportOptions.WithDefaults));
        return path;
    }

    /// <summary>Exporta a la ruta, sustituyendo el archivo si ya existe y comprobando antes que Windows la admite.</summary>
    internal static void ExportTo(string path, Action<FileInfo> export)
    {
        if (FileRules.IsPathTooLong(path))
            throw new ItemFailureException(FailureCode.PathTooLong, $"{path.Length} characters");
        var file = new FileInfo(path);
        if (file.Exists)
            file.Delete();
        export(file);
    }

    /// <summary>
    /// El programa entero de un PLC (bloques, tablas de variables y tipos) en las subcarpetas Bloques, Variables y
    /// Tipos, como lo lee GST-Diag. No compila nada: lo protegido o sin compilar sale como fallo.
    /// </summary>
    public BatchResult ExportProgram(ExportProgramArgs args)
    {
        RequireFolder(args.Folder);
        var plc = _project.FindPlc(args.Plc);
        var blocks = new List<PlcBlock>();
        CollectBlocks(plc.BlockGroup, blocks);
        var tables = new List<PlcTagTable>();
        CollectTagTables(plc.TagTableGroup, tables);
        var types = new List<PlcType>();
        CollectTypes(plc.TypeGroup, types);

        string root = Path.GetFullPath(args.Folder);
        foreach (string sub in new[] { "Bloques", "Variables", "Tipos" })
        {
            string dir = Path.Combine(root, sub);
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
        }

        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        int total = blocks.Count + tables.Count + types.Count;
        int done = 0;

        bool Export(string name, string sub, Action<FileInfo> export)
        {
            int index = done;
            Report(ProgressStage.Exporting, ++done, total, name);
            return batch.Run(name, total - done, () =>
            {
                string path = FileRules.FreeXmlPath(Path.Combine(root, sub), name);
                ExportTo(path, export);
                result.Files.Add(new ExportedFile { Index = index, Item = name, Path = path });
            });
        }

        foreach (var block in blocks)
        {
            if (!Export(block.Name, "Bloques", file => ExportProgramBlock(block, file)))
                return result;
        }
        foreach (var table in tables)
        {
            if (!Export(table.Name, "Variables", file => table.Export(file, ExportOptions.WithDefaults)))
                return result;
        }
        foreach (var type in types)
        {
            if (!Export(type.Name, "Tipos", file => type.Export(file, ExportOptions.WithDefaults)))
                return result;
        }
        return result;
    }

    private static void ExportProgramBlock(PlcBlock block, FileInfo file)
    {
        if (block.IsKnowHowProtected)
            throw new ItemFailureException(FailureCode.Protected);
        if (!block.IsConsistent)
            throw new ItemFailureException(FailureCode.NotCompiled);
        block.Export(file, ExportOptions.WithDefaults);
    }

    // ------------------------------------------------------------------ Importar

    /// <summary>
    /// Importa cada XML en su PLC y carpeta (las que faltan se crean), sobrescribiendo el bloque que tenga el mismo
    /// nombre.
    /// </summary>
    public BatchResult ImportBlocks(ImportBlocksArgs args)
    {
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        for (int i = 0; i < args.Files.Count; i++)
        {
            var file = args.Files[i];
            string item = Path.GetFileName(file.Path);
            Report(ProgressStage.Importing, i + 1, args.Files.Count, item);
            bool goOn = batch.Run(item, args.Files.Count - i - 1, () =>
            {
                RequireFile(file.Path);
                var group = TargetGroup(file);
                var imported = group.Blocks.Import(new FileInfo(file.Path), ImportOptions.Override);
                if (!args.CompileAfter)
                    return;
                foreach (PlcBlock block in imported)
                {
                    Report(ProgressStage.Compiling, i + 1, args.Files.Count, block.Name);
                    if (!CompileBlock(block))
                        batch.AddFailure(block.Name, FailureCode.DoesNotCompile);
                }
            });
            if (!goOn)
                break;
        }
        return result;
    }

    // Con UseExistingFolder, la carpeta del bloque que ya tiene el PLC con ese nombre; si no, la pedida.
    private PlcBlockGroup TargetGroup(ImportFile file)
    {
        IReadOnlyList<string> folders = file.Folders;
        if (file.UseExistingFolder && FileRules.ReadObjectName(file.Path) is { } name)
        {
            var existing = FindByName(_project.FindPlc(file.Plc).BlockGroup, name, new List<string>());
            if (existing != null)
                folders = existing;
        }
        return _project.FindBlockGroup(file.Plc, folders, create: true)!;
    }

    // Carpetas del bloque con ese nombre (sin distinguir mayúsculas), o null si no está.
    private static List<string>? FindByName(PlcBlockGroup group, string name, List<string> path)
    {
        foreach (PlcBlock block in group.Blocks)
        {
            if (block.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return path.ToList();
        }
        foreach (PlcBlockGroup sub in group.Groups)
        {
            path.Add(sub.Name);
            var found = FindByName(sub, name, path);
            path.RemoveAt(path.Count - 1);
            if (found != null)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Tipos de datos (UDT). Un XML se importa en su carpeta de tipos; una fuente (.udt, .scl…) se sube como fuente
    /// externa, se generan sus tipos (en la raíz de tipos: TIA no deja elegir) y se borra la fuente.
    /// </summary>
    public BatchResult ImportTypes(ImportFilesArgs args)
    {
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        for (int i = 0; i < args.Files.Count; i++)
        {
            var file = args.Files[i];
            string item = Path.GetFileName(file.Path);
            Report(ProgressStage.Importing, i + 1, args.Files.Count, item);
            bool goOn = batch.Run(item, args.Files.Count - i - 1, () =>
            {
                RequireFile(file.Path);
                var plc = _project.FindPlc(file.Plc);
                if (SourceExtensions.Contains(Path.GetExtension(file.Path).ToLowerInvariant()))
                    GenerateFromSource(plc, file.Path);
                else
                    TypeGroup(plc, file.Folders).Types.Import(new FileInfo(file.Path), ImportOptions.Override);
            });
            if (!goOn)
                break;
        }
        return result;
    }

    private static void GenerateFromSource(PlcSoftware plc, string path)
    {
        string name = Path.GetFileName(path);
        var sources = plc.ExternalSourceGroup.ExternalSources;
        sources.Find(name)?.Delete();
        PlcExternalSource source = sources.CreateFromFile(name, path);
        try
        {
            source.GenerateBlocksFromSource();
        }
        finally
        {
            source.Delete();
        }
    }

    private static PlcTypeGroup TypeGroup(PlcSoftware plc, IReadOnlyList<string> folders)
    {
        PlcTypeGroup group = plc.TypeGroup;
        foreach (string name in folders)
            group = group.Groups.Find(name) ?? (PlcTypeGroup)group.Groups.Create(name);
        return group;
    }

    /// <summary>Tablas de variables del PLC, en su carpeta (las que faltan se crean).</summary>
    public BatchResult ImportTagTables(ImportFilesArgs args)
    {
        var result = new BatchResult();
        var batch = _project.NewBatch(result);
        for (int i = 0; i < args.Files.Count; i++)
        {
            var file = args.Files[i];
            string item = Path.GetFileName(file.Path);
            Report(ProgressStage.Importing, i + 1, args.Files.Count, item);
            bool goOn = batch.Run(item, args.Files.Count - i - 1, () =>
            {
                RequireFile(file.Path);
                PlcTagTableGroup group = _project.FindPlc(file.Plc).TagTableGroup;
                foreach (string name in file.Folders)
                    group = group.Groups.Find(name) ?? (PlcTagTableGroup)group.Groups.Create(name);
                group.TagTables.Import(new FileInfo(file.Path), ImportOptions.Override);
            });
            if (!goOn)
                break;
        }
        return result;
    }

    // ------------------------------------------------------------------ Compilar

    public CompileResult Compile(CompileArgs args)
    {
        var results = new List<CompilerResult>();
        switch (args.Scope)
        {
            case CompileScope.Blocks:
                for (int i = 0; i < args.Blocks.Count; i++)
                {
                    Report(ProgressStage.Compiling, i + 1, args.Blocks.Count, args.Blocks[i].Name);
                    var block = _project.FindBlockGroup(args.Blocks[i].Plc, args.Blocks[i].Folders, create: false)?.Blocks.Find(args.Blocks[i].Name)
                        ?? throw new TiaConnectorException(ErrorCode.NotFound, $"Block not found: {args.Blocks[i]}");
                    AddIfAny(results, block.GetService<ICompilable>()?.Compile());
                }
                break;
            case CompileScope.Plc:
                Report(ProgressStage.Compiling, 0, 0, args.Plc);
                AddIfAny(results, _project.FindPlc(args.Plc).GetService<ICompilable>()?.Compile());
                break;
            default:
                var plcs = _project.Plcs().ToList();
                for (int i = 0; i < plcs.Count; i++)
                {
                    Report(ProgressStage.Compiling, i + 1, plcs.Count, plcs[i].Name);
                    AddIfAny(results, plcs[i].GetService<ICompilable>()?.Compile());
                }
                break;
        }
        return Summarize(results);
    }

    private static void AddIfAny(List<CompilerResult> results, CompilerResult? result)
    {
        if (result != null)
            results.Add(result);
    }

    private static CompileResult Summarize(IReadOnlyList<CompilerResult> results)
    {
        var summary = new CompileResult
        {
            Errors = results.Sum(r => r.ErrorCount),
            Warnings = results.Sum(r => r.WarningCount),
        };
        foreach (var result in results)
            CollectMessages(result.Messages, summary.Messages);
        summary.State = summary.Errors > 0 ? CompileState.Error : summary.Warnings > 0 ? CompileState.Warning : CompileState.Success;
        return summary;
    }

    // Solo las hojas con error o aviso: los nodos intermedios repiten lo mismo resumido.
    private static void CollectMessages(CompilerResultMessageComposition messages, List<CompileMessage> into)
    {
        foreach (CompilerResultMessage message in messages)
        {
            if (message.Messages.Count > 0)
                CollectMessages(message.Messages, into);
            else if (message.State == CompilerResultState.Error || message.State == CompilerResultState.Warning)
            {
                into.Add(new CompileMessage
                {
                    State = message.State == CompilerResultState.Error ? CompileState.Error : CompileState.Warning,
                    Path = message.Path,
                    Description = message.Description,
                });
            }
        }
    }

    /// <summary>Compila el bloque; devuelve si ha quedado compilado.</summary>
    private static bool CompileBlock(PlcBlock block)
    {
        block.GetService<ICompilable>()?.Compile();
        return block.IsConsistent;
    }

    // ------------------------------------------------------------------ Comunes

    private static void CollectBlocks(PlcBlockGroup group, List<PlcBlock> blocks)
    {
        blocks.AddRange(group.Blocks);
        foreach (PlcBlockGroup sub in group.Groups)
            CollectBlocks(sub, blocks);
    }

    private static void CollectTagTables(PlcTagTableGroup group, List<PlcTagTable> tables)
    {
        tables.AddRange(group.TagTables);
        foreach (PlcTagTableUserGroup sub in group.Groups)
            CollectTagTables(sub, tables);
    }

    private static void CollectTypes(PlcTypeGroup group, List<PlcType> types)
    {
        types.AddRange(group.Types);
        foreach (PlcTypeUserGroup sub in group.Groups)
            CollectTypes(sub, types);
    }

    internal static void RequireFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            throw new TiaConnectorException(ErrorCode.InvalidRequest, "folder is required.");
    }

    internal static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new ItemFailureException(FailureCode.NotFound, path);
    }

    private void Report(ProgressStage stage, int current, int total, string item) =>
        _progress.Report(new ProgressInfo { Stage = stage, Current = current, Total = total, Item = item });
}
