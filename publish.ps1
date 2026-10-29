# Publie le Compresseur de documents sous forme d'exécutables autonomes (aucune installation de .NET requise).
# Usage : .\publish.ps1 [rid...]    (par défaut : win-x64 linux-x64)
# Résultat : publish\<rid>\DocCompressor.exe (Windows) ou publish\<rid>\DocCompressor (Linux)
param([string[]]$Rids = @("win-x64", "linux-x64"))
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

foreach ($rid in $Rids) {
    dotnet publish DocCompressor/DocCompressor.csproj -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:GenerateDocumentationFile=false `
        -o "publish/$rid"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
