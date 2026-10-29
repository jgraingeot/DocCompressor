#!/usr/bin/env bash
# Publie le Compresseur de documents sous forme d'exécutables autonomes (aucune installation de .NET requise).
# Usage : ./publish.sh [rid...]    (par défaut : linux-x64 win-x64)
# Résultat : publish/<rid>/DocCompressor (Linux) ou publish/<rid>/DocCompressor.exe (Windows)
set -euo pipefail
cd "$(dirname "$0")"

if [ $# -eq 0 ]; then
  set -- linux-x64 win-x64
fi

for rid in "$@"; do
  dotnet publish DocCompressor/DocCompressor.csproj -c Release -r "$rid" --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:GenerateDocumentationFile=false \
    -o "publish/$rid"
done
