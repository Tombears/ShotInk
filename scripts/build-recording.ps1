[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot 'build\recording'
$cache = Join-Path $projectRoot 'build\recording-downloads'
New-Item -ItemType Directory -Force -Path $output, $cache | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-RecordingPackage($Name, $Url, $Digest) {
    $archive = Join-Path $cache $Name
    if (-not (Test-Path -LiteralPath $archive) -or (Get-FileHash -LiteralPath $archive).Hash -ne $Digest) {
        $temporary = $archive + '.download'
        $client = New-Object Net.WebClient
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            $client.DownloadFile($Url, $temporary)
            if ((Get-FileHash -LiteralPath $temporary).Hash -ne $Digest) { throw ('Recording dependency checksum mismatch for ' + $Name) }
            Move-Item -LiteralPath $temporary -Destination $archive -Force
        } finally { $client.Dispose(); if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    return $archive
}
function Copy-RecordingEntry($Package, $Entry, $Name) {
    $item = $Package.GetEntry($Entry)
    if ($null -eq $item) { throw ('Recording package omitted ' + $Entry) }
    $source = $item.Open(); $destination = [IO.File]::Create((Join-Path $output $Name))
    try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
}
$audio = Get-RecordingPackage 'naudio-1.10.0.nupkg' 'https://www.nuget.org/api/v2/package/NAudio/1.10.0' 'E4A80EED41DAD794695F9C114432DE4667D20BDFA80825AA94D159708CF29217'
$package = [IO.Compression.ZipFile]::OpenRead($audio)
try { Copy-RecordingEntry $package 'lib/net35/NAudio.dll' 'NAudio.dll' } finally { $package.Dispose() }
$video = Get-RecordingPackage 'ffmpeg-9.0.2.zip' 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.2/ffmpeg-9.0.2-essentials_build.zip' '60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA'
$package = [IO.Compression.ZipFile]::OpenRead($video)
try {
    Copy-RecordingEntry $package 'ffmpeg-9.0.2-essentials_build/bin/ffmpeg.exe' 'ffmpeg.exe'
    Copy-RecordingEntry $package 'ffmpeg-9.0.2-essentials_build/LICENSE' 'FFmpeg-LICENSE.txt'
    Copy-RecordingEntry $package 'ffmpeg-9.0.2-essentials_build/README.txt' 'FFmpeg-README.txt'
} finally { $package.Dispose() }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses\NAudio.txt') -Destination (Join-Path $output 'NAudio-LICENSE.txt') -Force
Write-Host 'Recording dependencies: NAudio 1.10.0, FFmpeg 9.0.2'
