$ErrorActionPreference = 'Stop'
$applicationPath = Join-Path $PSScriptRoot 'dist\SnipasteLite.exe'
if (-not (Test-Path -LiteralPath $applicationPath)) { & (Join-Path $PSScriptRoot 'build.ps1') }
Start-Process -FilePath $applicationPath -WorkingDirectory $PSScriptRoot
