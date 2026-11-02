[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = '1.0.4314.35'
$digest = '90778deb1b77e36292828b4b5833c9255c27533f40e0ddd55891ae718944edc4'
$cacheRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } elseif (Test-Path -LiteralPath 'E:\DevData') { 'E:\DevData\NuGet\packages' } else { Join-Path $env:LOCALAPPDATA 'NuGet\packages' }
$cache = Join-Path $cacheRoot ('microsoft.web.webview2\' + $version)
$archive = Join-Path $cache ('microsoft.web.webview2.' + $version + '.nupkg')
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $digest) {
    $temporary = $archive + '.download'
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri ('https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/' + $version) -OutFile $temporary -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temporary).Hash -ne $digest) { throw 'WebView2 SDK checksum mismatch.' }
        Move-Item -LiteralPath $temporary -Destination $archive -Force
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = Join-Path $projectRoot 'build\webview2'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$package = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($item in @(
        @('lib/net462/Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Core.dll'),
        @('lib/net462/Microsoft.Web.WebView2.WinForms.dll', 'Microsoft.Web.WebView2.WinForms.dll'),
        @('runtimes/win-x64/native/WebView2Loader.dll', 'WebView2Loader.dll'),
        @('LICENSE.txt', 'WebView2-LICENSE.txt')
    )) {
        $entry = $package.GetEntry($item[0]); if ($null -eq $entry) { throw ('WebView2 SDK omitted ' + $item[0]) }
        $inputStream = $entry.Open(); $outputStream = [IO.File]::Create((Join-Path $output $item[1]))
        try { $inputStream.CopyTo($outputStream) } finally { $outputStream.Dispose(); $inputStream.Dispose() }
        foreach ($directory in @('build', 'dist')) {
            $destination = Join-Path $projectRoot $directory
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $output $item[1]) -Destination $destination -Force
        }
    }
} finally { $package.Dispose() }
Write-Host ('WebView2 SDK: ' + $version)
