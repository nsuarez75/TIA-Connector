# TIA-Connector — Especificación

Redactada el 10/11/2026. Estado: **propuesta**. Antes de programar se cierra con una entrevista; las preguntas
abiertas están al final.

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
App (.NET 10 o 4.8) ──► TiaConnector.Cliente ──(JSON por líneas, stdin/stdout)──► TiaConnector.Puente.exe (4.8 x64) ──► TiaConnector.Core ──► Openness ──► TIA Portal
                                 │                                                           │
                                 └──────────────────── TiaConnector.Contratos ───────────────┘
```

| Proyecto | Marco | Qué es |
|---|---|---|
| `TiaConnector.Contratos` | .NET Standard 2.0 | Modelos (proyecto, PLC, HMI, bloque, resultado…) y mensajes del protocolo. Sin Openness. Lo usan el puente y el cliente |
| `TiaConnector.Core` | .NET Framework 4.8, x64 | Toda la lógica con Openness: lo que hoy está en `Services/` de HerramientasSICAR, más lo que añaden los exportadores de GST-Diag y GST-LoS |
| `TiaConnector.Puente` | .NET Framework 4.8, x64, exe | Proceso que sigue abierto mientras la app lo necesita. Lee peticiones por la entrada estándar y contesta por la salida estándar. Hilo único de Openness |
| `TiaConnector.Cliente` | .NET Standard 2.0 | Lo que usan las apps: métodos `async` normales. Lleva el puente dentro, lo extrae, lo arranca, le habla y lo cierra |
| `TiaConnector.Tests` | .NET 10 + 4.8 | Pruebas sin TIA: contratos y protocolo, cliente contra un puente falso, lógica de XML y de nombres del núcleo |

Una app .NET Framework 4.8 (HerramientasSICAR) puede usar el cliente igual que una .NET 10. Así su migración a .NET 10
deja de depender de Openness.

## 5. Protocolo puente ⇄ cliente

- Una línea JSON por mensaje, UTF-8 sin BOM, sin saltos de línea dentro.
- **Petición** (cliente → puente): `{"id": 7, "op": "exportar", "args": {...}}`.
- **Respuestas** (puente → cliente), todas con el `id` de su petición:
  - `{"id":7,"tipo":"progreso","texto":"Exportando 3/12: Estacion_FB"}` (las que haga falta);
  - `{"id":7,"tipo":"aviso","texto":"..."}`;
  - y una sola final: `{"id":7,"tipo":"resultado","datos":{...}}` o `{"id":7,"tipo":"error","texto":"...","codigo":"TiaCerrado"}`.
- Las peticiones se atienden **de una en una**, en orden (Openness va por un solo hilo). El cliente las encola.
- Al arrancar, el puente escribe `{"tipo":"listo","version":"1.0.0","tia":"V19"}`. Si no encuentra TIA V19 con Openness,
  escribe un error y termina.
- Al cerrarse la entrada estándar, el puente cierra solo los TIA sin ventana que abrió él y termina.
- Códigos de error comunes: `SinOpenness`, `SinPermiso` (el usuario no aceptó o no está en el grupo), `NoConectado`,
  `ProyectoNoAbierto`, `TiaCerrado`, `NoExiste`, `Protegido`, `NoCompila`, `RutaLarga`, `Interno`.

## 6. Operaciones de la versión 1

Cubren lo que usan hoy las tres apps. Un bloque se identifica siempre por **PLC, carpetas desde la raíz y nombre**
(`BloqueRef`), nunca solo por nombre.

### Sesión y proyecto
| Operación | Entrada | Salida | Viene de |
|---|---|---|---|
| `version` | — | versión del puente y de TIA | nueva |
| `proyectosAbiertos` | — | por cada TIA con proyecto: PID, ruta del proyecto | todas |
| `conectar` | ruta del proyecto; `abrirSinVentana` (si no está abierto en ningún TIA) | PLC y HMI (solo nombres), idioma de edición, carpeta del proyecto | HSICAR, GST-Diag |
| `desconectar` | — | — | nueva |
| `arbol` | PLC y HMI a leer | carpetas y bloques (nombre, tipo, número, protegido) de cada PLC; ramas y carpetas de cada HMI | HSICAR, GST-LoS |

### Bloques
| Operación | Entrada | Salida |
|---|---|---|
| `buscarBloques` | PLC; filtro: carpeta (a cualquier profundidad), nombre (patrón), tipo | lista de `BloqueRef` con protegido |
| `buscarPorNombre` | nombre | `BloqueRef` en todos los PLC |
| `exportarBloques` | bloques; carpeta; `compilarSiHaceFalta` (sí por defecto); `estructura`: `carpetas` (como en TIA), `plana` (numera si se repite) o `porBloque` | por bloque: ruta del XML o fallo |
| `importarBloques` | archivos con su PLC y carpeta destino; `crearCarpetas`; `compilarDespues` | por archivo: correcto, fallo o «importado pero no compila» |
| `compilar` | bloques, un PLC o todo el proyecto | resultado y errores de compilación |
| `exportarPrograma` | PLC; carpeta | bloques, tablas de variables y tipos en `Bloques`, `Variables` y `Tipos`, sin compilar nada; lo protegido o sin compilar sale como saltado |
| `importarTipos` | archivos de UDT con su PLC y carpeta | por archivo |
| `importarVariables` | archivos de tablas de variables con su PLC y carpeta | por archivo |

### HMI (WinCC clásicas; Unified fuera)
| Operación | Entrada | Salida |
|---|---|---|
| `leerRamasHmi` | HMI y ramas | carpetas y elementos |
| `exportarHmi` | elementos de HMI; carpeta | por elemento |
| `importarHmi` | archivos con su HMI, rama y carpetas | por archivo, en el orden de dependencias de `ArchivosHmi` |

### En el cliente (sin operación propia en el puente)
- `ModificarBloquesAsync(bloques, cambio, compilar)`: exporta a una carpeta temporal, aplica a cada XML la función de
  la app (que puede ser `async`, por ejemplo para traducir), importa encima y borra la carpeta. Es el ciclo que hoy usan
  el Numerador, Renumerar Array y el Comentador.
- `ElegirPlc`/`ElegirBloque` **no** van aquí: son diálogos y se quedan en cada app.

### Resultados y fallos
- Toda operación sobre varios objetos devuelve un resultado común: correctos y fallos «nombre: motivo».
- Si TIA se cierra a mitad, se para y el resultado dice cuántos quedan sin hacer (`TiaCerrado`), como `Intentar` en
  HerramientasSICAR.

## 7. Distribución

- `Contratos` y `Cliente` se usan como paquete NuGet desde una carpeta local o como submódulo de git (pregunta
  abierta). Sin GitHub Packages ni GitHub Actions.
- El cliente lleva `TiaConnector.Puente.exe` como recurso. La primera vez lo extrae a
  `%LOCALAPPDATA%\TIA-Connector\puente\<versión>\` (como GST-Diag). Si la versión del puente no es la que espera, el
  cliente lo vuelve a extraer.
- Versionado semántico. El protocolo lleva versión: un cliente no habla con un puente de versión mayor distinta.

## 8. Plan

1. **Repo y núcleo:** `Contratos`, `Core` (traído de `Services/` de HerramientasSICAR, ya ordenado por la PR #28), `Puente`, `Cliente`, tests, `checks.sh` (compilación con avisos como errores + tests, sin TIA).
2. **GST-Diag** pasa al cliente y se borra `GstDiag.Exporter`. Es la prueba más sencilla: solo `proyectosAbiertos`, `conectar` y `exportarPrograma`.
3. **GST-LoS-Generator** pasa al cliente y se borra `GstLosGenerator.Tia`.
4. **HerramientasSICAR** pasa al cliente y se borra su `Services/`. Después, si se quiere, se migra a .NET 10.
5. Se retira el repo ImportExportTIA.

Cada paso, en su repo, con issue, rama, `checks.sh` y PR.

## 9. Fuera de la versión 1

- TIA Portal V20 o posteriores. Se deja preparado: lo dependiente de la versión va en el puente. Antes de V20, comprobar si Openness V20 ya funciona en .NET moderno, porque entonces el puente podría sobrar en esa versión.
- HMI Unified, configuración de hardware, online (cargar en el PLC, observar), Version Control Interface.

## 10. Entrevista

Respuestas del 10/11/2026:

1. **Distribución:** pendiente. Requisito: cada app se reparte como **un solo ejecutable** (el puente va dentro del cliente y se extrae solo, §7).
2. **Abrir el proyecto sin ventana:** sí, en la versión 1, como opción de `conectar` que cada app usa o no.
3. **Varias apps a la vez:** cada app arranca su propio puente; no hay puente compartido.
4. **Idioma del código:** inglés (nombres de operaciones, modelos, clases y miembros). Los nombres de operaciones de §5 y §6 se pasan a inglés al programar el paso 1.
5. **Registro:** sí. El puente escribe en `%LOCALAPPDATA%\TIA-Connector\logs\`, un archivo por día, y borra los de más de 14 días. Solo operaciones, nombres de bloques y errores; nunca contenido de los proyectos.
6. **Licencia:** MIT, y el repo pasa a ser público.
