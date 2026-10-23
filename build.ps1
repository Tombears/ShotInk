[CmdletBinding()]
param([switch]$Test, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'scripts\build-inputs.ps1')
$sourceSignature = Get-ShotInkBuildSignature -ProjectRoot $projectRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 on Windows first.' }
$buildRoot = Join-Path $projectRoot 'build'
$distRoot = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $buildRoot, $distRoot -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll', '/r:Accessibility.dll')
$common = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/platform:anycpu', '/warn:4') + $references
$appPath = Join-Path $distRoot 'ShotInk.exe'
& $compiler @common '/target:winexe' ('/out:' + $appPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/win32icon:' + (Join-Path $projectRoot 'assets\shotink.ico')) ('/resource:' + (Join-Path $projectRoot 'assets\shotink.ico') + ',ShotInk.ApplicationIcon') @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $distRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $distRoot -Force
$docsRoot = Join-Path $distRoot 'docs'
New-Item -ItemType Directory -Path $docsRoot -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\ocr.md') -Destination $docsRoot -Force
& (Join-Path $projectRoot 'scripts\build-ocr.ps1')
if ($Test -or $Preview) {
    $testPath = Join-Path $buildRoot 'ShotInk.Tests.exe'
    $testSource = Join-Path $projectRoot 'tests\Tests.cs'
    & $compiler @common '/target:exe' '/main:ShotInk.Tests' ('/out:' + $testPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/resource:' + (Join-Path $projectRoot 'assets\shotink.ico') + ',ShotInk.ApplicationIcon') @sources $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $testArgs = @()
    if ($Preview) { $testArgs += @('--preview', $buildRoot) }
    & $testPath @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Test) {
        & (Join-Path $projectRoot 'tests\Run.Tests.ps1')
        & (Join-Path $projectRoot 'tests\Release.Tests.ps1') -BinaryPath $appPath
    }
}
$archive = Join-Path $projectRoot 'ShotInk-portable.zip'
Compress-Archive -LiteralPath $appPath, (Join-Path $distRoot 'README.md'), (Join-Path $distRoot 'LICENSE'), (Join-Path $distRoot 'ocr'), $docsRoot -DestinationPath $archive -Force
[IO.File]::WriteAllText((Join-Path $distRoot 'ShotInk.build.sha256'), $sourceSignature)
Write-Host ('Built: ' + $appPath)
Write-Host ('Portable: ' + $archive)
