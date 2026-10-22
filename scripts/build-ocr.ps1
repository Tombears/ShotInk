[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ocr')
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$hostRoot = Join-Path $projectRoot 'ocr-host'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Building the local OCR component requires .NET SDK 8 or later. Portable-package users do not need the SDK.' }
$modelRoot = Join-Path $hostRoot 'models\v6'
New-Item -ItemType Directory -Path $modelRoot -Force | Out-Null
$manifest = Get-Content -LiteralPath (Join-Path $hostRoot 'models.lock.json') -Raw | ConvertFrom-Json
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
foreach ($model in $manifest.files) {
    $target = Join-Path $modelRoot $model.name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $model.sha256) { continue }
    $temporary = $target + '.download'
    try {
        Invoke-WebRequest -Uri $model.url -OutFile $temporary -UseBasicParsing
        if ((Get-Item -LiteralPath $temporary).Length -ne $model.bytes -or (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $model.sha256) { throw ('OCR model checksum failed: ' + $model.name) }
        Move-Item -LiteralPath $temporary -Destination $target -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
$staging = Join-Path $projectRoot ('build\ocr-' + $Runtime)
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
& dotnet publish (Join-Path $hostRoot 'ShotInk.OcrHost.csproj') -c Release -r $Runtime --self-contained true --disable-build-servers -p:UseSharedCompilation=false -p:RestoreLockedMode=true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'OCR component publication failed.' }
# The package copies its default Latin models too. Only the orientation model
# is required by InitModels; this host explicitly selects bilingual v6 models.
foreach ($unused in @('ch_PP-OCRv5_mobile_det.onnx', 'latin_PP-OCRv5_rec_mobile_infer.onnx', 'ppocrv5_latin_dict.txt')) {
    $path = Join-Path $staging ('models\v5\' + $unused)
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
}
Copy-Item -LiteralPath (Join-Path $hostRoot 'models.lock.json') -Destination $staging -Force
Copy-Item -LiteralPath (Join-Path $hostRoot 'packages.lock.json') -Destination $staging -Force
if (Test-Path -LiteralPath $OutputDirectory) { Remove-Item -LiteralPath $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $staging '*') -Destination $OutputDirectory -Recurse -Force
Write-Host ('OCR component: ' + $OutputDirectory)
