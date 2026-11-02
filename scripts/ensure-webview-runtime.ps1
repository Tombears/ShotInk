$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'build\Microsoft.Web.WebView2.Core.dll')
try { $version = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::GetAvailableBrowserVersionString() } catch { $version = $null }
if ($version) { Write-Host ('WebView2 runtime: ' + $version); return }
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Install the Microsoft Edge WebView2 Evergreen Runtime before running browser integration tests.' }
$bootstrapper = Join-Path $projectRoot 'build\MicrosoftEdgeWebview2Setup.exe'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper -UseBasicParsing
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') { throw 'WebView2 bootstrapper signature verification failed.' }
$process = Start-Process -FilePath $bootstrapper -ArgumentList '/silent', '/install' -Wait -PassThru -WindowStyle Hidden
if ($process.ExitCode -notin @(0, 3010)) { throw ('WebView2 runtime installation failed: ' + $process.ExitCode) }
$version = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::GetAvailableBrowserVersionString()
if (-not $version) { throw 'WebView2 runtime is still unavailable.' }
Write-Host ('WebView2 runtime: ' + $version)
