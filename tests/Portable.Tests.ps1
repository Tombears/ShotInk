param([switch]$Lite,[switch]$FullOnly)
$ErrorActionPreference = 'Stop'
if(-not $Lite -and -not $FullOnly){& $PSCommandPath -FullOnly;& $PSCommandPath -Lite;return}
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$fixture = Join-Path $temporaryRoot ('ShotInk-portable-tests-' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $root $(if($Lite){'ShotInk-portable-lite.zip'}else{'ShotInk-portable.zip'})
$dist=Join-Path $root $(if($Lite){'dist-lite'}else{'dist'})
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $fixture)
    $required=@('ShotInk.exe','ShotInk.flavor','ShotInk.build.sha256','update-portable.ps1','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')
    if(-not $Lite){$required+=@('ocr\ShotInk.OcrHost.exe','ocr\coreclr.dll','ocr\onnxruntime.dll','ocr\libSkiaSharp.dll','ocr\models\v6\PP-OCRv6_rec_small.onnx')}
    foreach($file in $required){if(-not(Test-Path -LiteralPath (Join-Path $fixture $file))){throw ('ZIP omitted '+$file)}}
    if(Test-Path -LiteralPath (Join-Path $fixture 'docs')){throw 'Portable ZIP retained docs.'}
    if($Lite -and (Test-Path -LiteralPath (Join-Path $fixture 'ocr'))){throw 'Lite ZIP retained OCR files.'}
    if ((Get-FileHash -LiteralPath (Join-Path $fixture 'ShotInk.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $dist 'ShotInk.exe')).Hash) { throw 'Packaged application differs from the tested build.' }
    Add-Type -AssemblyName System.Drawing
    $applicationIcon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $fixture 'ShotInk.exe'))
    try {
        $iconPixels = $applicationIcon.ToBitmap()
        try {
            $yellow = 0
            for ($y = 0; $y -lt $iconPixels.Height; $y++) { for ($x = 0; $x -lt $iconPixels.Width; $x++) {
                $color = $iconPixels.GetPixel($x, $y)
                if ($color.R -gt 180 -and $color.G -gt 100 -and $color.B -lt 150) { $yellow++ }
            } }
            if ($yellow -lt 20 -or $iconPixels.GetPixel(0, 0).A -ne 0) { throw 'Packaged executable lost its yellow pen icon or transparency.' }
        } finally { $iconPixels.Dispose() }
    } finally { $applicationIcon.Dispose() }
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
    $probe = Join-Path $fixture 'ShotInk.PackageTests.exe'
    $defines=@();if($Lite){$defines+='/define:SHOTINK_LITE'}
    $testSource=Join-Path $PSScriptRoot $(if($Lite){'Lite.Package.Tests.cs'}else{'Installed.Tests.cs'})
    & $compiler /nologo /utf8output /codepage:65001 /target:exe /main:ShotInk.InstalledTests ('/out:' + $probe) /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:Accessibility.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ('/r:' + (Join-Path $fixture 'Microsoft.Web.WebView2.Core.dll')) ('/r:' + (Join-Path $fixture 'Microsoft.Web.WebView2.WinForms.dll')) @defines @sources $testSource
    if ($LASTEXITCODE -ne 0) { throw 'Packaged OCR probe compilation failed.' }
    & $probe (Join-Path $PSScriptRoot 'fixtures\ocr-mixed.png') $archive
    if ($LASTEXITCODE -ne 0) { throw 'Extracted ZIP cannot find or run its bundled OCR environment.' }
    if (-not (Get-Process -Name ShotInk -ErrorAction SilentlyContinue)) {
        $application = Start-Process -FilePath (Join-Path $fixture 'ShotInk.exe') -WorkingDirectory $fixture -WindowStyle Hidden -PassThru
        try {
            Start-Sleep -Seconds 2
            if ($application.HasExited) { throw 'Packaged application exited on launch.' }
        } finally { if (-not $application.HasExited) { Stop-Process -Id $application.Id -Force } }
    } else { Write-Host 'Existing ShotInk instance preserved; separate application launch skipped.' }
    Write-Host 'PASS: portable ZIP contents, application hash, executable icon and bundled OCR inference'
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
