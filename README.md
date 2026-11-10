# TIA-Connector

Conexión común con TIA Portal (Openness) para todas nuestras apps. Openness V19 solo funciona en .NET Framework 4.8,
así que el acceso a TIA vive aquí, en un **puente** 4.8 que cada app lanza y con el que habla mediante una librería
**cliente** que vale para .NET 10 y para .NET Framework 4.8.

La especificación completa (operaciones, protocolo, decisiones) está en [`docs/especificacion.md`](docs/especificacion.md).

## Cómo lo usa una app

1. Añadir el repo como submódulo y referenciar el cliente:

   ```
   git submodule add https://github.com/nsuarez75/TIA-Connector.git external/TIA-Connector
   ```

   ```xml
   <ProjectReference Include="..\..\external\TIA-Connector\src\TiaConnector.Client\TiaConnector.Client.csproj" />
   ```

2. Usarlo:

   ```csharp
   using TiaConnector.Client;
   using TiaConnector.Contracts;

   using var tia = await TiaClient.StartAsync();                 // extrae y arranca el puente
   var open = await tia.GetOpenProjectsAsync();
   var project = await tia.ConnectAsync(open[0].ProjectPath);   // la primera vez TIA pide permiso al usuario
   var plcs = project.Devices.Where(d => d.Kind == DeviceKind.Plc).Select(d => d.Name);
   var tree = await tia.ReadTreeAsync(plcs, []);
   var sequences = BlockSearch.InFolder(tree.Plcs[0], "10_Sequence & Messageblocks");
   var result = await tia.ModifyBlocksAsync(sequences, async (block, xml) => Change(xml), compile: true);
   ```

   Cada operación sobre varios elementos devuelve un `BatchResult` (correctos, fallos con su código y archivos
   escritos). Los errores de una petición entera llegan como `TiaConnectorException` con su `ErrorCode`. Los textos del
   puente van en inglés: la app compone sus mensajes a partir de los códigos.

3. Para actualizar: `git submodule update --remote external/TIA-Connector`, commit en la app y compilar.

La app se sigue repartiendo como un solo ejecutable: el puente va dentro del cliente y se extrae solo a
`%LOCALAPPDATA%\TIA-Connector\bridge\<versión>-<huella>\`.

## Reglas con TIA

- Nunca se cierra el TIA del usuario: solo el TIA sin ventana que abre el propio puente (`OpenMode.WithoutWindow`).
- La primera vez que el puente se engancha, TIA pregunta al usuario si lo deja entrar. El usuario tiene que estar en el
  grupo de Windows **Siemens TIA Openness**.
- Openness no exporta bloques protegidos (know-how). Los bloques sin compilar se compilan antes de exportarlos, salvo
  en `ExportProgramAsync`, que no cambia nada del proyecto.
- Nunca se sobrescribe la tabla de variables estándar de una HMI (ha llegado a cerrar TIA).

## Estructura

| Carpeta | Qué hay |
|---|---|
| `src/TiaConnector.Contracts` | Modelos, protocolo y reglas de archivos sin TIA (.NET Standard 2.0) |
| `src/TiaConnector.Core` | Todo lo que se hace con Openness (.NET Framework 4.8 x64) |
| `src/TiaConnector.Bridge` | El puente: un `.exe` que habla JSON por líneas por la entrada y salida estándar |
| `src/TiaConnector.Client` | `TiaClient` para las apps; lleva el puente dentro |
| `tests/TiaConnector.Tests` | Pruebas sin TIA, en .NET 10 y en .NET Framework 4.8 |
| `tools/TiaConnector.Smoke` | Prueba a mano contra un TIA abierto |

El código va en inglés y los comentarios en castellano.

## Comprobaciones

```
./checks.sh
```

Compila en Release con los avisos como errores y pasa los tests. No necesita TIA abierto, pero sí TIA Portal V19
instalado para compilar el núcleo (igual que las apps). Las pruebas arrancan el puente de verdad sin engancharse a
ningún TIA.

Prueba a mano contra un TIA abierto (solo lee y exporta a una carpeta temporal):

```
dotnet run --project tools/TiaConnector.Smoke -c Release -- [ruta.ap19] [--program] [--write]
```

`--program` exporta además el programa entero del primer PLC; `--write` vuelve a importar sin cambios el primer FB que
no sea de seguridad y lo compila (toca el proyecto: guarda antes en TIA).

El puente deja un registro en `%LOCALAPPDATA%\TIA-Connector\logs\` (un archivo por día, 14 días).

## Licencia

MIT. Ver [LICENSE](LICENSE).
