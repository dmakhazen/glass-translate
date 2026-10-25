# Builds the portable version: dist\GlassTranslate\ (run GlassTranslate.exe) and a zip of it.
# Needs the .NET 10 SDK; downloads the OCR models into models_cache\ on first run.
param([string]$Version = '0.1.0')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\GlassTranslate'
& (Join-Path $root 'tools\get-models.ps1')

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root\src\GlassTranslate" -c Release -o $out -p:DebugType=none -p:Version=$Version -nologo
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# Import libraries and the crash-dump helper aren't needed at run time.
Get-ChildItem $out -Filter *.lib | Remove-Item
Remove-Item (Join-Path $out 'createdump.exe') -ErrorAction SilentlyContinue

$zip = Join-Path $root "dist\GlassTranslate-$Version-portable-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path $out -DestinationPath $zip
Write-Host "Done: $zip"
