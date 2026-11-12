# TIA-Connector

Vocabulario del repo. El código va en inglés: entre
paréntesis, el nombre en el código.

**Puente** (`TiaConnector.Bridge`):
Programa .NET Framework 4.8 (`TiaConnector.Bridge.exe`) que habla con TIA Portal mediante Openness. Lo arranca el
**cliente** y sigue abierto mientras la app lo necesita.
_Evitar_: exportador, worker, servidor

**Cliente** (`TiaClient`, proyecto `TiaConnector.Client`):
Librería que usan las apps para pedir cosas al **puente**. Vale para .NET 10 y para .NET Framework 4.8.

**Petición** (`Request`):
Orden que el cliente manda al puente (una línea JSON con `id`, `op` y `args`). Tiene una sola respuesta final:
resultado o error.

**BlockRef**:
Cómo se identifica un bloque: PLC, carpetas desde la raíz de bloques y nombre. Nunca solo por nombre: con varios PLC
el nombre se repite.
_Antes_: BloqueRef

**Resultado de lote** (`BatchResult`):
Lo que devuelve toda operación sobre varios elementos: cuántos salieron bien, los fallos con su código
(`FailureCode`) y los archivos escritos. Si TIA se cierra a mitad, dice cuántos quedaron sin hacer.

**TIA sin ventana** (`OpenMode.WithoutWindow`):
Instancia de TIA Portal que abre el propio puente, sin interfaz, cuando el proyecto no está abierto en ningún TIA. Es
la única que el puente cierra.
