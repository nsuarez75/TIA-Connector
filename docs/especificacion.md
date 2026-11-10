# TIA-Connector — Especificación

Redactada el 10/11/2026. Estado: **cerrada** tras la entrevista del 10/11/2026 (respuestas en §10).

## 1. Objetivo

Un único sitio para todo lo que nuestras apps hacen con TIA Portal mediante Openness, para:

- no repetir el mismo código en cada app (hoy hay tres versiones, ver §2);
- poder escribir las apps en **.NET 10** aunque Openness V19 solo funcione en **.NET Framework 4.8**;
- que un cambio en el acceso a TIA (un fallo, una versión nueva de TIA) se haga una sola vez.

Norma que hereda de HerramientasSICAR (10/11/2026): todo lo relacionado con TIA está encapsulado y es común a todas
las herramientas; ninguna app llama a Openness por su cuenta.

## 2. Lo que hay hoy

| App | Dónde | Forma | Qué hace con TIA |
|---|---|---|---|
| HerramientasSICAR (.NET 4.8) | `src/HerramientasSICAR/Services` | En el mismo proceso, un hilo propio (`HiloOpenness`) | Proyectos abiertos, conectar, árbol de PLC y HMI, exportar e importar bloques y elementos de HMI, compilar, ciclo exportar → cambiar XML → importar (`ProyectoTia.Bloques.cs`), programa entero para Sincro Checker, idioma de edición |
| GST-Diag (.NET 10) | `src/GstDiag.Exporter` | Exe 4.8 que se lanza para cada orden y contesta por la salida estándar («CLAVE texto») | `projects` (TIA abiertos y sus PLC), `export` del programa entero de un PLC; si el proyecto no está abierto, lo abre en un TIA sin ventana y lo cierra al acabar |
| GST-LoS-Generator (.NET 10) | `src/GstLosGenerator.Tia` | Exe 4.8 que sigue abierto mientras la app lo necesita; órdenes por la entrada estándar, campos separados por tabuladores | `projects`, `tree` (PLC, carpetas y bloques), `export` de un bloque, `import` de UDT + FB, compilar y volver a exportar el FB |
| ImportExportTIA | repo propio | — | Ya integrado en HerramientasSICAR; se retira |

Las tres coinciden en lo básico: cargar `Siemens.Engineering.dll` de la instalación de TIA V19 (registro de
Openness o ruta estándar), engancharse a un TIA del usuario sin cerrarlo nunca, y exportar o importar XML.

## 3. Restricciones de TIA Openness

- Openness V19 = .NET Framework 4.8, x64. `Siemens.Engineering.dll` **no se copia** con el programa: se carga de la
  instalación de TIA V19 (registro `SOFTWARE\Siemens\Automation\Openness\19.0\PublicAPI\V19`, o
  `C:\Program Files\Siemens\Automation\Portal V19\PublicAPI\V19`).
- El usuario de Windows tiene que estar en el grupo **Siemens TIA Openness**. La primera vez que un programa se
  engancha, TIA pregunta si le deja entrar.
- **Nunca** se hace `Dispose` de un `TiaPortalProcess` ni del `TiaPortal` enganchado: cierra el TIA del usuario. Solo
  se cierran los TIA sin ventana que haya abierto el propio puente.
- Todas las llamadas a Openness van por un único hilo.
- Openness no exporta bloques protegidos (know-how) ni sin compilar.

## 4. Arquitectura

```
App (.NET 10 o 4.8) ──► TiaConnector.Client ──(JSON por líneas, stdin/stdout)──► TiaConnector.Bridge.exe (4.8 x64) ──► TiaConnector.Core ──► Openness ──► TIA Portal
                                 │                                                           │
                                 └──────────────────── TiaConnector.Contracts ───────────────┘
```

| Proyecto | Marco | Qué es |
|---|---|---|
| `TiaConnector.Contracts` | .NET Standard 2.0 | Modelos (proyecto, PLC, HMI, bloque, resultado…), mensajes del protocolo y reglas de archivos sin TIA (`FileRules`, `HmiRules`, `TempFolder`). Lo usan el puente, el cliente y las apps |
| `TiaConnector.Core` | .NET Framework 4.8, x64 | Toda la lógica con Openness (`TiaSession`, `ProjectAccess`, `BlockOperations`, `HmiOperations`): lo que estaba en `Services/` de HerramientasSICAR, más lo que añadían los exportadores de GST-Diag y GST-LoS |
| `TiaConnector.Bridge` | .NET Framework 4.8, x64, exe | El puente. Lee peticiones por la entrada estándar y contesta por la salida estándar. Hilo único de Openness. Un solo `.exe` (Costura) |
| `TiaConnector.Client` | .NET Standard 2.0 | Lo que usan las apps (`TiaClient`): métodos `async`. Lleva el puente dentro, lo extrae, lo arranca, le habla y lo cierra |
| `TiaConnector.Tests` | .NET 10 + 4.8 | Pruebas sin TIA: reglas de archivos, protocolo, cliente contra un puente falso y el puente de verdad extraído (sin engancharse a TIA) |
| `tools/TiaConnector.Smoke` | .NET 10, exe | Prueba a mano contra un TIA abierto, desde .NET 10 como lo usarán las apps |

Una app .NET Framework 4.8 (HerramientasSICAR) puede usar el cliente igual que una .NET 10. Así su migración a .NET 10
deja de depender de Openness.

## 5. Protocolo puente ⇄ cliente

- Una línea JSON por mensaje, UTF-8 sin BOM, sin saltos de línea dentro. Nombres en camelCase y enumerados como texto.
- **Petición** (cliente → puente): `{"id":7,"op":"exportBlocks","args":{...}}`.
- **Mensajes** (puente → cliente), todos con el `id` de su petición:
  - `{"id":7,"kind":"progress","progress":{"stage":"exporting","current":3,"total":12,"item":"Estacion_FB"}}` (los que haga falta);
  - `{"id":7,"kind":"warning","text":"..."}`;
  - y uno solo final: `{"id":7,"kind":"result","data":{...}}` o `{"id":7,"kind":"error","code":"tiaClosed","text":"..."}`.
- Los textos del puente van en inglés y sin traducir: la app se guía por los códigos (`stage`, `code`, `FailureCode`) y
  compone sus mensajes en su idioma.
- Las peticiones se atienden **de una en una**, en orden (Openness va por un solo hilo). El cliente puede mandar varias
  seguidas; cada respuesta vuelve a quien la pidió.
- Al arrancar, el puente escribe `{"kind":"ready","protocol":1,"bridgeVersion":"0.1.0"}`. Si no encuentra TIA V19 con
  Openness, escribe `{"kind":"error","code":"noOpenness",...}` y termina.
- Al cerrarse la entrada estándar, el puente termina lo pendiente, cierra solo el TIA sin ventana que abrió él y sale.
- Códigos de error (`ErrorCode`): `noOpenness`, `noPermission` (el usuario no aceptó o no está en el grupo),
  `notConnected`, `projectNotOpen`, `tiaClosed`, `notFound`, `protected`, `doesNotCompile`, `pathTooLong`,
  `invalidRequest`, `unknownOperation`, `bridgeExited` y `protocolMismatch` (solo del cliente), `internal`.

## 6. Operaciones de la versión 1

Cubren lo que usan hoy las tres apps. Un bloque se identifica siempre por **PLC, carpetas desde la raíz y nombre**
(`BlockRef`), nunca solo por nombre.

### Sesión y proyecto
| Operación | Entrada | Salida | Viene de |
|---|---|---|---|
| `version` | — | versión del puente, del protocolo y de TIA | nueva |
| `openProjects` | — | por cada TIA con proyecto: PID y ruta del proyecto | todas |
| `connect` | ruta del proyecto; `ifNotOpen`: `fail`, `withoutWindow` (TIA sin ventana del puente) o `withWindow` (como un doble clic; ese TIA sigue abierto) | nombre, ruta, carpeta, idioma de edición, PLC y HMI (solo nombres) | HSICAR, GST-Diag, GST-LoS |
| `disconnect` | — | — | nueva |
| `readTree` | PLC y HMI a leer | carpetas y bloques (nombre, tipo, número, protegido) de cada PLC; ramas y carpetas de cada HMI | HSICAR, GST-LoS |
| `readPlc` | PLC | su árbol de bloques | HSICAR |

### Bloques
| Operación | Entrada | Salida |
|---|---|---|
| `exportBlocks` | bloques; carpeta; `compileIfNeeded` (sí por defecto); `layout`: `folders` (como en TIA), `flat` (numera si se repite) o `perBlock` | `BatchResult` con la ruta de cada XML |
| `importBlocks` | archivos con su PLC y carpeta destino (se crean si faltan); `useExistingFolder` por archivo; `compileAfter` | `BatchResult`; «importado pero no compila» sale como fallo `doesNotCompile` |
| `compile` | bloques, un PLC o todo el proyecto | estado, errores, avisos y mensajes |
| `exportProgram` | PLC; carpeta | bloques, tablas de variables y tipos en `Bloques`, `Variables` y `Tipos`, sin compilar nada; lo protegido o sin compilar sale como fallo |
| `importTypes` | archivos de UDT (XML, o fuente `.udt`/`.scl` que se genera y se borra) con su PLC y carpeta | `BatchResult` |
| `importTagTables` | archivos de tablas de variables con su PLC y carpeta | `BatchResult` |

### HMI (WinCC clásicas; Unified fuera)
| Operación | Entrada | Salida |
|---|---|---|
| `readHmiBranches` | HMI y ramas | carpetas y elementos |
| `exportHmi` | elementos de HMI; carpeta | `BatchResult`; cada elemento en `<carpeta>\[HMI\]<rama>\<carpetas>\` |
| `importHmi` | HMI y archivos con su rama y carpetas | `BatchResult`, en el orden de dependencias de las ramas (`HmiBranch`); nunca sobrescribe la tabla de variables estándar |

### En el cliente (sin operación propia en el puente)
- `BlockSearch.Find` / `InFolder`: buscar bloques en un árbol ya leído (por carpeta a cualquier profundidad, nombre,
  tipo…). Sustituye a las operaciones `buscarBloques` y `buscarPorNombre` de la propuesta: filtrar en el cliente es más
  flexible que mandar el filtro al puente.
- `FindBlocksByNameAsync`: bloques con un nombre en todos los PLC.
- `ReadBlockXmlAsync`: XML de un bloque sin dejar nada en disco.
- `ModifyBlocksAsync(bloques, cambio, compilar)`: exporta a una carpeta temporal, aplica a cada XML la función de la app
  (que puede ser `async`, por ejemplo para traducir), importa encima y borra la carpeta. Es el ciclo que usan el
  Numerador, Renumerar Array y el Comentador.
- Elegir PLC o bloque **no** va aquí: son diálogos y se quedan en cada app.

### Resultados y fallos
- Toda operación sobre varios objetos devuelve `BatchResult`: cuántos salieron bien, los fallos (`item`, `code`,
  `detail` con el texto de TIA) y los archivos escritos.
- Si TIA se cierra a mitad, se para: `stopped` es true y `remaining` dice cuántos quedan sin hacer.

## 7. Distribución

- Cada app lleva TIA-Connector como **submódulo de git** (por ejemplo en `external/TIA-Connector`) y referencia
  `TiaConnector.Client` como proyecto; al compilar la app se compila también el puente (hace falta TIA V19 instalado
  para compilar, igual que hoy). Para actualizar una app, se trae el commit nuevo de TIA-Connector al submódulo, se
  hace commit en la app y se compila. Sin GitHub Packages ni GitHub Actions.
- Cada app se reparte como un solo ejecutable.
- El cliente lleva `TiaConnector.Bridge.exe` como recurso. La primera vez lo extrae a
  `%LOCALAPPDATA%\TIA-Connector\bridge\<versión>-<huella>\`; la huella cambia con cada compilación distinta del puente,
  así que dos apps con versiones distintas conviven y nunca se usa un puente viejo.
- Registro del puente: `%LOCALAPPDATA%\TIA-Connector\logs\bridge-AAAAMMDD.log`, 14 días.
- Versionado semántico (`Directory.Build.props`). El protocolo lleva su propia versión: un cliente no habla con un
  puente de otro protocolo (`protocolMismatch`).

## 8. Plan

1. **Repo y núcleo** (hecho, issue #4): `Contracts`, `Core` (traído de `Services/` de HerramientasSICAR), `Bridge`, `Client`, tests, `checks.sh` (compilación con avisos como errores + tests, sin TIA) y prueba contra un TIA real con `tools/TiaConnector.Smoke`.
2. **GST-Diag** pasa al cliente y se borra `GstDiag.Exporter`. Es la prueba más sencilla: solo `openProjects`, `connect` y `exportProgram`.
3. **GST-LoS-Generator** pasa al cliente y se borra `GstLosGenerator.Tia`.
4. **HerramientasSICAR** pasa al cliente y se borra su `Services/`. Después, si se quiere, se migra a .NET 10.
5. Se retira el repo ImportExportTIA.

Cada paso, en su repo, con issue, rama, `checks.sh` y PR.

## 9. Fuera de la versión 1

- TIA Portal V20 o posteriores. Se deja preparado: lo dependiente de la versión va en el puente. Antes de V20, comprobar si Openness V20 ya funciona en .NET moderno, porque entonces el puente podría sobrar en esa versión.
- HMI Unified, configuración de hardware, online (cargar en el PLC, observar), Version Control Interface.

## 10. Entrevista

Respuestas del 10/11/2026:

1. **Distribución:** submódulo de git en cada app (el repo es público, así que cualquiera puede clonar y compilar). Cada app se reparte como **un solo ejecutable**: el puente va dentro del cliente y se extrae solo (§7). Cuando TIA-Connector esté estable se puede pasar a paquete NuGet sin tocar el código de las apps.
2. **Abrir el proyecto sin ventana:** sí, en la versión 1, como opción de `conectar` que cada app usa o no.
3. **Varias apps a la vez:** cada app arranca su propio puente; no hay puente compartido.
4. **Idioma del código:** inglés (nombres de operaciones, modelos, clases y miembros). Los nombres de operaciones de §5 y §6 se pasan a inglés al programar el paso 1.
5. **Registro:** sí. El puente escribe en `%LOCALAPPDATA%\TIA-Connector\logs\`, un archivo por día, y borra los de más de 14 días. Solo operaciones, nombres de bloques y errores; nunca contenido de los proyectos.
6. **Licencia:** MIT, y el repo pasa a ser público.
