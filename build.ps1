[CmdletBinding()]
param([switch]$Test, [switch]$Preview, [switch]$Lite, [switch]$FullOnly)
$ErrorActionPreference = 'Stop'
if (-not $Lite -and -not $FullOnly) {
    & $PSCommandPath -Test:$Test -Preview:$Preview -FullOnly
    & $PSCommandPath -Test:$Test -Preview:$Preview -Lite
    return
}
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-Path $projectRoot 'scripts\build-inputs.ps1')
$sourceSignature = Get-ShotInkBuildSignature -ProjectRoot $projectRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 on Windows first.' }
$buildRoot = Join-Path $projectRoot $(if($Lite){'build\lite'}else{'build'})
$distRoot = Join-Path $projectRoot $(if($Lite){'dist-lite'}else{'dist'})
New-Item -ItemType Directory -Path $buildRoot, $distRoot -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& (Join-Path $projectRoot 'scripts\build-webview.ps1')
& (Join-Path $projectRoot 'scripts\build-recording.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\recording\NAudio.dll') -Destination $buildRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\recording\NAudio.dll') -Destination $distRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\recording') -Destination $distRoot -Recurse -Force
$duplicateAudioLibrary = Join-Path $distRoot 'recording\NAudio.dll'
if (Test-Path -LiteralPath $duplicateAudioLibrary) { Remove-Item -LiteralPath $duplicateAudioLibrary -Force }
if ($Lite) { Copy-Item -LiteralPath (Join-Path $projectRoot 'build\recording') -Destination $buildRoot -Recurse -Force }
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll','WebView2-LICENSE.txt')) {
    $dependency=Join-Path $projectRoot ('build\webview2\'+$name)
    Copy-Item -LiteralPath $dependency -Destination $buildRoot -Force
    Copy-Item -LiteralPath $dependency -Destination $distRoot -Force
}
$references = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Runtime.Serialization.dll', '/r:Accessibility.dll', '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
$references += '/r:' + (Join-Path $buildRoot 'NAudio.dll')
$references += @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll') | ForEach-Object { '/r:' + (Join-Path $buildRoot $_) }
$skinResources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'assets\skins') -Filter '*.png' | ForEach-Object { '/resource:' + $_.FullName + ',ShotInk.Skins.' + $_.Name })
$common = @('/nologo', '/utf8output', '/codepage:65001', '/optimize+', '/platform:anycpu', '/warn:4') + $references + $skinResources
if ($Lite) { $common += '/define:SHOTINK_LITE' }
$appPath = Join-Path $distRoot 'ShotInk.exe'
& $compiler @common '/target:winexe' ('/out:' + $appPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/win32icon:' + (Join-Path $projectRoot 'assets\shotink.ico')) ('/resource:' + (Join-Path $projectRoot 'assets\shotink.ico') + ',ShotInk.ApplicationIcon') @sources
if ($LASTEXITCODE -ne 0) { throw 'Application compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot $(if($Lite){'README-lite.md'}else{'README.md'})) -Destination (Join-Path $distRoot 'README.md') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $distRoot -Force
if (-not $Lite) { & (Join-Path $projectRoot 'scripts\build-ocr.ps1') }
if ($Test -or $Preview) {
    $testPath = Join-Path $buildRoot 'ShotInk.Tests.exe'
    $testSource = Join-Path $projectRoot $(if($Lite){'tests\Lite.Tests.cs'}else{'tests\Tests.cs'})
    $sharedTests = @('tests\GifTools.Tests.cs', 'tests\CaptureWorkflow.Tests.cs', 'tests\UpdateParts.Tests.cs', 'tests\Skin.Tests.cs') | ForEach-Object { Join-Path $projectRoot $_ }
    & $compiler @common '/target:exe' '/main:ShotInk.Tests' ('/out:' + $testPath) ('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')) ('/resource:' + (Join-Path $projectRoot 'assets\shotink.ico') + ',ShotInk.ApplicationIcon') @sources $testSource @sharedTests
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    $testArgs = @()
    if ($Preview) { $testArgs += @('--preview', $buildRoot) }
    & $testPath @testArgs
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Test -and -not $Lite) {
        & (Join-Path $projectRoot 'tests\Run.Tests.ps1')
        & (Join-Path $projectRoot 'tests\Release.Tests.ps1') -BinaryPath $appPath
        & (Join-Path $projectRoot 'tests\Update.Tests.ps1') -BinaryPath $appPath
        & (Join-Path $projectRoot 'tests\Update.Smoke.Tests.ps1')
    }
}
$archive = Join-Path $projectRoot $(if($Lite){'ShotInk-portable-lite.zip'}else{'ShotInk-portable.zip'})
if ($Lite) {
    $hash=[Security.Cryptography.SHA256]::Create()
    try {$sourceSignature=[BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes($sourceSignature+':lite'))).Replace('-','')}finally{$hash.Dispose()}
}
[IO.File]::WriteAllText((Join-Path $distRoot 'ShotInk.flavor'), $(if($Lite){'lite'}else{'ocr'}))
[IO.File]::WriteAllText((Join-Path $distRoot 'ShotInk.build.sha256'), $sourceSignature)
[IO.File]::WriteAllText((Join-Path $distRoot 'update-portable.ps1'), [IO.File]::ReadAllText((Join-Path $projectRoot 'scripts\update-portable.ps1')), (New-Object Text.UTF8Encoding($true)))
$browserFiles = @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll', 'WebView2Loader.dll', 'WebView2-LICENSE.txt') | ForEach-Object { Join-Path $distRoot $_ }
$files=@((Join-Path $distRoot 'recording'), (Join-Path $distRoot 'NAudio.dll'), $appPath, (Join-Path $distRoot 'README.md'), (Join-Path $distRoot 'LICENSE'), (Join-Path $distRoot 'ShotInk.build.sha256'), (Join-Path $distRoot 'ShotInk.flavor'), (Join-Path $distRoot 'update-portable.ps1')) + $browserFiles
if (-not $Lite) {$files+=Join-Path $distRoot 'ocr'}
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Write-Host ('Built: ' + $appPath)
Write-Host ('Portable: ' + $archive)
