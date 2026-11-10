#!/usr/bin/env bash
# Comprobaciones antes de cada commit: compila en Release (avisos = errores) y pasa los tests, sin TIA.
# Termina en 0 solo si todo está bien. No modifica archivos del repo (la salida va a bin/ y obj/, que no se suben).
# Necesita TIA Portal V19 instalado (Siemens.Engineering.dll) para compilar el núcleo, igual que las apps.
set -euo pipefail
cd "$(dirname "$0")"

echo "== Compilación"
dotnet build TiaConnector.sln -c Release -nologo -v quiet -warnaserror

echo "== Tests"
dotnet test TiaConnector.sln -c Release --no-build -nologo -v quiet

echo "== Todo correcto"
