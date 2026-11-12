# Pendientes

Los errores y peticiones nuevas van como issues de GitHub. Aquí, lo que queda abierto sin issue.

- Probar contra TIA lo que no probó `tools/TiaConnector.Smoke` el 10/11/2026: abrir un proyecto cerrado sin ventana y
  con ventana, `importTypes`, `importTagTables`, `importHmi` y `compile`. Lo probado: conectar, leer el árbol (PLC y 5
  HMI), XML de un bloque, exportar bloques, listas de textos y el programa entero, y reimportar y compilar un FB.
  Desde el 12/11/2026 HerramientasSICAR usa `importHmi` y `compile` (Importar / Exportar, Generador HMI, Comentador y
  Numerador) y el usuario las ha probado; falta confirmar `importTypes`, `importTagTables` y abrir un proyecto cerrado.
- Paso 2 hecho el 10/11/2026: GST-Diag usa el cliente como submódulo (GST-Diag PR #98) y ya no tiene `GstDiag.Exporter`.
- Paso 4 hecho el 12/11/2026: HerramientasSICAR usa el cliente (HerramientasSicar PR #45) y ya no tiene `Services/`;
  después pasó a .NET 10 y Avalonia 12 (PR #47).
- Paso 3: GST-LoS-Generator, que aún tiene su propio `GstLosGenerator.Tia`.
- Paso 5: el repo ImportExportTIA ya no existe en GitHub; queda su carpeta local `Importar Exportar TIA`.
