$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $temporaryRoot ('ShotInk-portable-tests-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $root 'ShotInk-portable.zip'
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $fixture)
    foreach ($file in @('ShotInk.exe', 'ocr\ShotInk.OcrHost.exe', 'ocr\coreclr.dll', 'ocr\onnxruntime.dll', 'ocr\libSkiaSharp.dll', 'ocr\models\v6\PP-OCRv6_rec_small.onnx')) {
        if (-not (Test-Path -LiteralPath (Join-Path $fixture $file))) { throw ('ZIP omitted required file: ' + $file) }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $fixture 'ShotInk.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'dist\ShotInk.exe')).Hash) { throw 'Packaged application differs from the tested build.' }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
    $probe = Join-Path $fixture 'ShotInk.PackageTests.exe'
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /main:ShotInk.InstalledTests ('/out:' + $probe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:Accessibility.dll @sources (Join-Path $PSScriptRoot 'Installed.Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Packaged OCR probe compilation failed.' }
    & $probe (Join-Path $PSScriptRoot 'fixtures\ocr-mixed.png')
    if ($LASTEXITCODE -ne 0) { throw 'Extracted ZIP cannot find or run its bundled OCR environment.' }
    if (-not (Get-Process -Name ShotInk -ErrorAction SilentlyContinue)) {
        $application = Start-Process -FilePath (Join-Path $fixture 'ShotInk.exe') -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
        try {
            Start-Sleep -Seconds 2
            if ($application.HasExited) { throw 'Packaged application exited on launch.' }
        } finally { if (-not $application.HasExited) { Stop-Process -Id $application.Id -Force } }
    } else { Write-Host 'Existing ShotInk instance preserved; separate application launch skipped.' }
    Write-Host 'PASS: portable ZIP contents, application hash and bundled OCR inference'
} finally {
    if (Test-Path -LiteralPath $fixture) {
        $resolvedFixture = (Resolve-Path -LiteralPath $fixture).Path
        if (-not $resolvedFixture.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing to clean a path outside the test temporary directory.' }
        # Process.Kill returns before Windows has released every OCR DLL handle.
        for ($attempt = 0; $attempt -lt 40; $attempt++) {
            try { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction Stop; break }
            catch {
                if ($attempt -eq 39 -or ($_.Exception -isnot [IO.IOException] -and $_.Exception -isnot [UnauthorizedAccessException])) { throw }
                Start-Sleep -Milliseconds 100
            }
        }
    }
}
