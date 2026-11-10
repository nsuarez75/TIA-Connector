# TIA-Connector

Vocabulario del repo. El común a todas las apps está en el `GLOSSARY.md` de GST-Tools.

**Puente**:
Programa .NET Framework 4.8 (`TiaConnector.Puente.exe`) que habla con TIA Portal mediante Openness. Lo arranca el
**cliente** y sigue abierto mientras la app lo necesita.
_Evitar_: exportador, worker, servidor

**Cliente**:
Librería que usan las apps para pedir cosas al **puente**. Vale para .NET 10 y para .NET Framework 4.8.

**Petición**:
Orden que el cliente manda al puente (una línea JSON con `id`, `op` y `args`). Tiene una sola respuesta final:
resultado o error.

**BloqueRef**:
Cómo se identifica un bloque: PLC, carpetas desde la raíz de bloques y nombre. Nunca solo por nombre: con varios PLC
el nombre se repite.

**TIA sin ventana**:
Instancia de TIA Portal que abre el propio puente, sin interfaz, cuando el proyecto no está abierto en ningún TIA. Es
la única que el puente cierra.
