[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$redist = Join-Path $root 'build\redist'
New-Item -ItemType Directory -Path $redist -Force | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$dependencies = @(
    @{ Name = 'NDP48-x86-x64-AllOS-ENU.exe'; Url = 'https://go.microsoft.com/fwlink/?linkid=2088631'; MinimumBytes = 50000000 },
    @{ Name = 'VC_redist.x64.exe'; Url = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'; MinimumBytes = 10000000 }
)
foreach ($dependency in $dependencies) {
    $path = Join-Path $redist $dependency.Name
    if (-not (Test-Path -LiteralPath $path)) {
        $temporary = $path + '.download'
        try {
            Invoke-WebRequest -Uri $dependency.Url -OutFile $temporary -UseBasicParsing
            Move-Item -LiteralPath $temporary -Destination $path -Force
        } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
    }
    if ((Get-Item -LiteralPath $path).Length -lt $dependency.MinimumBytes) { throw ('Incomplete offline runtime installer: ' + $dependency.Name) }
    $signature = Get-AuthenticodeSignature -FilePath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|,\s*)O=Microsoft Corporation(?:,|$)') { throw ('Invalid Microsoft signature: ' + $dependency.Name) }
}
$compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
if ($compiler) { $iscc = $compiler.Source } else {
    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $iscc) { throw 'Install Inno Setup 6.5 or later to build ShotInk-Setup.exe.' }
$metadata = Get-Content -LiteralPath (Join-Path $root 'src\AssemblyInfo.cs') -Raw
if ($metadata -notmatch 'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)') { throw 'Missing application version.' }
$version = $Matches[1]
$vcFile = Join-Path $redist 'VC_redist.x64.exe'
$vcVersionText = (Get-Item -LiteralPath $vcFile).VersionInfo.FileVersion
if ($vcVersionText -notmatch '(\d+\.\d+\.\d+\.\d+)') { throw 'Cannot read bundled Visual C++ runtime version.' }
$vcVersion = [version]$Matches[1]
& $iscc ('/DSourceRoot=' + $root) ('/DAppVersion=' + $version) ('/DVcMajor=' + $vcVersion.Major) ('/DVcMinor=' + $vcVersion.Minor) ('/DVcBuild=' + $vcVersion.Build) ('/DVcRevision=' + $vcVersion.Revision) (Join-Path $root 'installer\ShotInk.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Host ('Installer: ' + (Join-Path $root 'ShotInk-Setup.exe'))
