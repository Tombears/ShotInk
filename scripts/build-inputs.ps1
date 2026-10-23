function Get-ShotInkBuildSignature {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)
    $inputs = @('build.ps1', 'run.ps1', 'ShotInk.csproj', 'app.manifest', 'README.md', 'LICENSE')
    $inputs += @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'scripts') -Filter '*.ps1' | ForEach-Object { 'scripts\' + $_.Name })
    $ocrRoot = Join-Path $ProjectRoot 'ocr-host'
    if (Test-Path -LiteralPath $ocrRoot) {
        $inputs += @(Get-ChildItem -LiteralPath $ocrRoot -File | ForEach-Object { 'ocr-host\' + $_.Name })
        $inputs += @(Get-ChildItem -LiteralPath (Join-Path $ocrRoot 'licenses') -File | ForEach-Object { 'ocr-host\licenses\' + $_.Name })
    }
    $ocrDoc = Join-Path $ProjectRoot 'docs\ocr.md'
    if (Test-Path -LiteralPath $ocrDoc) { $inputs += 'docs\ocr.md' }
    $installerRoot = Join-Path $ProjectRoot 'installer'
    if (Test-Path -LiteralPath $installerRoot) { $inputs += @(Get-ChildItem -LiteralPath $installerRoot -File | ForEach-Object { 'installer\' + $_.Name }) }
    $inputs += @(Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'src') -Filter '*.cs' | ForEach-Object { 'src\' + $_.Name })
    $icon = Join-Path $ProjectRoot 'assets\shotink.ico'
    if (Test-Path -LiteralPath $icon) { $inputs += 'assets\shotink.ico' }
    $hashes = foreach ($relative in ($inputs | Sort-Object)) {
        $digest = Get-FileHash -LiteralPath (Join-Path $ProjectRoot $relative) -Algorithm SHA256
        $relative + ':' + $digest.Hash
    }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes(($hashes -join "`n"))
        return [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '')
    } finally { $algorithm.Dispose() }
}
