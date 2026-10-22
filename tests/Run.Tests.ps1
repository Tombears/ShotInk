$ErrorActionPreference = 'Stop'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-launch-tests-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Split-Path -Parent $PSScriptRoot
$state = [pscustomobject]@{ BuildCount = 0; LaunchCount = 0; SimulateRunning = $false }
function Assert-Launch([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Start-Process {
    param([string]$FilePath, [string]$WorkingDirectory)
    Assert-Launch (Test-Path -LiteralPath $FilePath) 'Launcher started a missing application.'
    $state.LaunchCount++
}
function Get-Process {
    param([string[]]$Name, $ErrorAction)
    if ($state.SimulateRunning) { [pscustomobject]@{ ProcessName = 'ShotInk' } }
}
try {
    New-Item -ItemType Directory -Path $fixture, (Join-Path $fixture 'src'), (Join-Path $fixture 'scripts'), (Join-Path $fixture 'dist\ocr') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'run.ps1') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\build-inputs.ps1') -Destination (Join-Path $fixture 'scripts')
    foreach ($file in @('ShotInk.csproj', 'app.manifest', 'README.md', 'LICENSE', 'src\Sample.cs')) {
        [IO.File]::WriteAllText((Join-Path $fixture $file), 'fixture')
    }
    $fakeBuild = @'
Register-TestBuild
. (Join-Path $PSScriptRoot 'scripts\build-inputs.ps1')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\ShotInk.exe'), 'current binary')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\ocr\ShotInk.OcrHost.exe'), 'current OCR component')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\ShotInk.build.sha256'), (Get-ShotInkBuildSignature -ProjectRoot $PSScriptRoot))
'@
    # Record the real launcher's build invocation without running a compiler.
    function Register-TestBuild { $state.BuildCount++ }
    [IO.File]::WriteAllText((Join-Path $fixture 'build.ps1'), $fakeBuild)
    $run = Join-Path $fixture 'run.ps1'
    & $run
    Assert-Launch ($state.BuildCount -eq 1 -and $state.LaunchCount -eq 1) 'First launch did not build and start the app.'
    & $run
    Assert-Launch ($state.BuildCount -eq 1 -and $state.LaunchCount -eq 2) 'Unchanged source caused an unnecessary rebuild.'
    $source = Join-Path $fixture 'src\Sample.cs'
    $timestamp = [IO.File]::GetLastWriteTimeUtc($source)
    [IO.File]::WriteAllText($source, 'updated source')
    [IO.File]::SetLastWriteTimeUtc($source, $timestamp)
    & $run
    Assert-Launch ($state.BuildCount -eq 2 -and $state.LaunchCount -eq 3) 'Changed source with an unchanged timestamp reused the old EXE.'
    Remove-Item -LiteralPath (Join-Path $fixture 'dist\ShotInk.build.sha256')
    & $run
    Assert-Launch ($state.BuildCount -eq 3 -and $state.LaunchCount -eq 4) 'An EXE without a build signature was treated as current.'
    Remove-Item -LiteralPath (Join-Path $fixture 'dist\ocr\ShotInk.OcrHost.exe')
    & $run
    Assert-Launch ($state.BuildCount -eq 4 -and $state.LaunchCount -eq 5) 'A missing OCR component was not rebuilt.'
    [IO.File]::WriteAllText($source, 'next change')
    $state.SimulateRunning = $true
    $blocked = $false
    try { & $run } catch { $blocked = $_.Exception.Message -like '*system tray*' }
    Assert-Launch ($blocked -and $state.BuildCount -eq 4 -and $state.LaunchCount -eq 5) 'A running app was overwritten or the old binary was launched after a failed build.'
    Write-Host 'PASS: 6 launcher checks'
} finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force } }
