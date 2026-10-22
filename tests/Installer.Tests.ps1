$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-install-tests-' + [Guid]::NewGuid().ToString('N'))
$log = Join-Path $root 'build\installer-test.log'
$installer = Join-Path $root 'ShotInk-Setup.exe'
$uninstaller = Join-Path $fixture 'unins000.exe'
try {
    $setup = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/DIR="' + $fixture + '"'), ('/LOG="' + $log + '"')) -Wait -PassThru
    if ($setup.ExitCode -notin @(0, 3010)) { throw ('Complete installer failed: ' + $setup.ExitCode) }
    foreach ($file in @('ShotInk.exe', 'ocr\ShotInk.OcrHost.exe', 'ocr\coreclr.dll', 'ocr\onnxruntime.dll', 'ocr\libSkiaSharp.dll', 'ocr\models\v6\PP-OCRv6_rec_small.onnx')) {
        if (-not (Test-Path -LiteralPath (Join-Path $fixture $file))) { throw ('Installer omitted required file: ' + $file) }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $fixture 'ShotInk.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'dist\ShotInk.exe')).Hash) { throw 'Installed application differs from the tested build.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
    $probe = Join-Path $fixture 'ShotInk.InstalledTests.exe'
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /main:ShotInk.InstalledTests ('/out:' + $probe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:Accessibility.dll @sources (Join-Path $PSScriptRoot 'Installed.Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Installed OCR probe compilation failed.' }
    & $probe (Join-Path $PSScriptRoot 'fixtures\ocr-mixed.png')
    if ($LASTEXITCODE -ne 0) { throw 'Installed OCR cannot find or run its bundled environment.' }
    Remove-Item -LiteralPath $probe -Force
    $application = Start-Process -FilePath (Join-Path $fixture 'ShotInk.exe') -PassThru
    try {
        Start-Sleep -Seconds 2
        if ($application.HasExited) { throw 'Installed application exited on launch.' }
        Write-Host 'PASS: complete offline package installation, bundled OCR inference and application startup'
    } finally { if (-not $application.HasExited) { Stop-Process -Id $application.Id -Force } }
} finally {
    if (Test-Path -LiteralPath $uninstaller) {
        $removed = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
        if ($removed.ExitCode -ne 0) { Write-Warning ('Uninstaller exit code: ' + $removed.ExitCode) }
    }
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
