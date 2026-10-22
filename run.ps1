$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\build-inputs.ps1')
$applicationPath = Join-Path $PSScriptRoot 'dist\ShotInk.exe'
$stampPath = Join-Path $PSScriptRoot 'dist\ShotInk.build.sha256'
$signature = Get-ShotInkBuildSignature -ProjectRoot $PSScriptRoot
$builtSignature = if (Test-Path -LiteralPath $stampPath) { [IO.File]::ReadAllText($stampPath).Trim() } else { '' }
if (-not (Test-Path -LiteralPath $applicationPath) -or -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'dist\ocr\ShotInk.OcrHost.exe')) -or $builtSignature -ne $signature) {
    if (Get-Process -Name 'ShotInk', 'SnipasteLite' -ErrorAction SilentlyContinue) {
        throw 'ShotInk is running. Exit it from the system tray, then run this script again to build the current source.'
    }
    & (Join-Path $PSScriptRoot 'build.ps1')
}
Start-Process -FilePath $applicationPath -WorkingDirectory $PSScriptRoot
