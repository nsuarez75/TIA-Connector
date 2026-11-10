// Prueba a mano de TIA-Connector contra un TIA Portal abierto, desde .NET 10.
//
//   dotnet run --project tools/TiaConnector.Smoke -- [ruta.ap19] [--program] [--write]
//
// Sin ruta, usa el primer proyecto abierto. Solo lee y exporta a una carpeta temporal, salvo:
//   --program  exporta además el programa entero del primer PLC (puede tardar);
//   --write    vuelve a importar sin cambios el primer FB y lo compila (toca el proyecto: guarda antes en TIA).
using TiaConnector.Client;
using TiaConnector.Contracts;

string? projectPath = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));
bool exportProgram = args.Contains("--program");
bool write = args.Contains("--write");
var progress = new ConsoleProgress();

using var client = await TiaClient.StartAsync();
client.Warning += text => Console.WriteLine($"  aviso: {text}");
var version = await client.GetVersionAsync();
Console.WriteLine($"Puente {version.Bridge}, protocolo {version.Protocol}, TIA {version.Tia}");

var open = await client.GetOpenProjectsAsync();
foreach (var project in open)
    Console.WriteLine($"Abierto: {project.ProjectPath} (PID {project.ProcessId})");
projectPath ??= open.FirstOrDefault()?.ProjectPath ?? throw new InvalidOperationException("No hay ningún proyecto abierto en TIA.");

var info = await client.ConnectAsync(projectPath, OpenMode.Fail, progress);
Console.WriteLine($"Conectado a {info.Name}; idioma de edición {info.EditingLanguage}; {string.Join(", ", info.Devices.Select(d => $"{d.Name} ({d.Kind})"))}");

var plcs = info.Devices.Where(d => d.Kind == DeviceKind.Plc).Select(d => d.Name).ToList();
var hmis = info.Devices.Where(d => d.Kind == DeviceKind.Hmi).Select(d => d.Name).ToList();
var tree = await client.ReadTreeAsync(plcs, hmis, progress);
foreach (var plc in tree.Plcs)
    Console.WriteLine($"PLC {plc.Name}: {BlockSearch.Find(plc, (_, _) => true).Count} bloques");
foreach (var hmi in tree.Hmis)
    Console.WriteLine($"HMI {hmi.Name}: {string.Join(", ", hmi.Branches.Select(b => $"{b.Branch} {Count(b.Root)}"))}");

using var temp = new TempFolder();
// Nunca un bloque de seguridad (F): reimportarlo pide la contraseña de seguridad y recompilar el programa F.
var fb = tree.Plcs.SelectMany(p => BlockSearch.Find(p, (folders, b) => b.Type == "FB" && !b.IsProtected
        && !b.Name.StartsWith("F_", StringComparison.OrdinalIgnoreCase)
        && !folders.Any(f => f.Contains("Safety", StringComparison.OrdinalIgnoreCase))))
    .FirstOrDefault();
if (fb != null)
{
    string xml = await client.ReadBlockXmlAsync(fb);
    Console.WriteLine($"XML de {fb}: {xml.Length} caracteres");
}

var someBlocks = tree.Plcs.SelectMany(p => BlockSearch.Find(p, (_, b) => !b.IsProtected)).Take(5).ToList();
var exported = await client.ExportBlocksAsync(new ExportBlocksArgs { Blocks = someBlocks, Folder = temp.Path }, progress);
Report("Exportar 5 bloques", exported);

if (tree.Hmis.FirstOrDefault() is { } firstHmi)
{
    var items = firstHmi.Branches.Where(b => b.Branch == HmiBranch.TextLists)
        .SelectMany(b => b.Root.Items.Take(3).Select(name => new HmiItemRef { Hmi = firstHmi.Name, Branch = b.Branch, Name = name }))
        .ToList();
    Report("Exportar 3 listas de textos", await client.ExportHmiAsync(new ExportHmiArgs { Items = items, Folder = temp.Path }, progress));
}

if (exportProgram && plcs.Count > 0)
    Report($"Exportar el programa de {plcs[0]}", await client.ExportProgramAsync(plcs[0], Path.Combine(temp.Path, "programa"), progress));

if (write && fb != null)
    Report($"Reimportar {fb} sin cambios", await client.ModifyBlocksAsync(new[] { fb }, (_, xml) => Task.FromResult(xml), compile: true, progress));

await client.DisconnectAsync();
Console.WriteLine("Desconectado. Todo correcto.");

static int Count(HmiFolder folder) => folder.Items.Count + folder.Folders.Sum(Count);

static void Report(string what, BatchResult result)
{
    Console.WriteLine($"{what}: {result.Succeeded} bien, {result.Failures.Count} fallos{(result.Stopped ? $", parado con {result.Remaining} pendientes" : "")}");
    foreach (var failure in result.Failures)
        Console.WriteLine($"  {failure.Item}: {failure.Code} {failure.Detail}");
}

internal sealed class ConsoleProgress : IProgress<ProgressInfo>
{
    public void Report(ProgressInfo value) =>
        Console.WriteLine(value.Total > 0 ? $"  {value.Stage} {value.Current}/{value.Total} {value.Item}" : $"  {value.Stage} {value.Item}");
}
