using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System.Collections.Generic;
using System.Linq;
using TiaConnector.Contracts;

namespace TiaConnector.Core;

/// <summary>Acceso a los dispositivos y carpetas de un proyecto abierto. Solo en el hilo de Openness.</summary>
public sealed class ProjectAccess
{
    public ProjectAccess(Project project)
    {
        Project = project;
    }

    public Project Project { get; }

    public bool IsAlive() => TiaSession.IsAlive(Project);

    /// <summary>Un ejecutor de lotes que para si TIA cierra el proyecto.</summary>
    public BatchRunner NewBatch(BatchResult result) => new(result, IsAlive);

    public IEnumerable<PlcSoftware> Plcs() => Software<PlcSoftware>().Select(s => s.Software);

    /// <summary>HMI WinCC clásicas (paneles y estaciones PC). Las Unified no salen: su software es de otro tipo.</summary>
    public IEnumerable<HmiTarget> Hmis() => Software<HmiTarget>().Select(s => s.Software);

    /// <summary>PLC y HMI con el nombre del dispositivo que los contiene (en un IPC, la estación PC).</summary>
    public List<DeviceInfo> Devices() =>
        Software<PlcSoftware>().Select(s => new DeviceInfo { Name = s.Software.Name, Kind = DeviceKind.Plc, Station = s.Station })
            .Concat(Software<HmiTarget>().Select(s => new DeviceInfo { Name = s.Software.Name, Kind = DeviceKind.Hmi, Station = s.Station }))
            .ToList();

    public PlcSoftware FindPlc(string name) =>
        Plcs().FirstOrDefault(p => p.Name == name)
        ?? throw new TiaConnectorException(ErrorCode.NotFound, $"PLC not found: {name}");

    public HmiTarget FindHmi(string name) =>
        Hmis().FirstOrDefault(h => h.Name == name)
        ?? throw new TiaConnectorException(ErrorCode.NotFound, $"HMI not found: {name}");

    /// <summary>
    /// Carpeta de bloques bajando desde la raíz del PLC. Si falta una: con <paramref name="create"/>, se crea; sin él,
    /// devuelve null.
    /// </summary>
    public PlcBlockGroup? FindBlockGroup(string plc, IReadOnlyList<string> folders, bool create)
    {
        PlcBlockGroup group = FindPlc(plc).BlockGroup;
        foreach (string name in folders)
        {
            PlcBlockGroup? next = group.Groups.Find(name);
            if (next == null)
            {
                if (!create)
                    return null;
                next = group.Groups.Create(name);
            }
            group = next;
        }
        return group;
    }

    // Cada software de ese tipo con el nombre del dispositivo (de primer nivel) en el que está.
    private IEnumerable<(T Software, string Station)> Software<T>() where T : class
    {
        var devices = new List<Device>(Project.Devices);
        var pending = new Queue<DeviceUserGroup>(Project.DeviceGroups);
        while (pending.Count > 0)
        {
            var group = pending.Dequeue();
            devices.AddRange(group.Devices);
            foreach (DeviceUserGroup sub in group.Groups)
                pending.Enqueue(sub);
        }

        foreach (var device in devices)
            foreach (var software in FindSoftware<T>(device.DeviceItems))
                yield return (software, device.Name);
    }

    private static IEnumerable<T> FindSoftware<T>(DeviceItemComposition items) where T : class
    {
        foreach (DeviceItem item in items)
        {
            if (item.GetService<SoftwareContainer>()?.Software is T software)
                yield return software;
            foreach (var inner in FindSoftware<T>(item.DeviceItems))
                yield return inner;
        }
    }
}
