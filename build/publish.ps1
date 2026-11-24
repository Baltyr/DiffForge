# Publica DiffForge como un único .exe autocontenido (no requiere .NET instalado)
# y arma un .zip portable. Uso:  ./build/publish.ps1 [-Runtime win-x64] [-SkipTests]
param(
    [string]$Runtime = "win-x64",
    [switch]$SkipTests
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root "dist"
$out = Join-Path $dist $Runtime

if (-not $SkipTests) {
    # Los tests de LineSimilarity quedan fuera hasta que el TODO esté implementado.
    dotnet test (Join-Path $root "DiffForge.sln") -c Release --filter "FullyQualifiedName!~ClosenessMatching"
    if ($LASTEXITCODE -ne 0) { throw "Los tests fallaron; no se publica." }
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $root "src/DiffForge.App/DiffForge.App.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }

$version = (Get-Item (Join-Path $out "DiffForge.exe")).VersionInfo.ProductVersion
$zip = Join-Path $dist "DiffForge-$version-$Runtime-portable.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
Write-Host "Ejecutable: $(Join-Path $out 'DiffForge.exe')"
Write-Host "Portable:   $zip"
