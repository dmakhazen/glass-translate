# Downloads the OCR models that ship inside the portable build into models_cache\ (skips files already there).
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$base = 'https://huggingface.co/monkt/paddleocr-onnx/resolve/main'
$files = @{ 'det\det.onnx' = 'detection/v5/det.onnx' }
foreach ($l in 'thai', 'english', 'eslav', 'latin') {
    foreach ($f in 'rec.onnx', 'dict.txt') { $files["$l\$f"] = "languages/$l/$f" }
}
foreach ($local in $files.Keys) {
    $target = Join-Path $root "models_cache\$local"
    if (Test-Path $target) { continue }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Write-Host "Downloading $local"
    Invoke-WebRequest "$base/$($files[$local])" -OutFile $target
}
