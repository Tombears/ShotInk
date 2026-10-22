param([string]$BinaryPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ShotInk.exe'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-release-tests-' + [Guid]::NewGuid().ToString('N'))
$originalRef = $env:GITHUB_REF
$originalRefName = $env:GITHUB_REF_NAME
$originalExitCode = $global:LASTEXITCODE
$state = @{ Calls = @(); Releases = @(); Head = 'current'; UploadFails = $false; Notes = '' }
function Assert-Release([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function gh {
    $arguments = @($args)
    $state.Calls += ,$arguments
    $global:LASTEXITCODE = 0
    if ($arguments -contains '--notes-file') { $state.Notes = [IO.File]::ReadAllText($arguments[[Array]::IndexOf($arguments, '--notes-file') + 1]) }
    if ($arguments[0] -eq 'api') {
        if ($arguments[1] -like '*/git/ref/heads/main') { return $state.Head }
        return ConvertTo-Json -InputObject @($state.Releases) -Depth 5 -Compress
    }
    if ($arguments[0] -eq 'release' -and $arguments[1] -eq 'upload' -and $state.UploadFails) { $global:LASTEXITCODE = 1 }
}
function Calls-For([string]$Verb) { return ,@($state.Calls | Where-Object { $_[0] -eq 'release' -and $_[1] -eq $Verb }) }
try {
    New-Item -ItemType Directory -Force -Path $fixture, (Join-Path $fixture 'scripts'), (Join-Path $fixture 'src'), (Join-Path $fixture 'releases'), (Join-Path $fixture 'assets\dist') | Out-Null
    Copy-Item -LiteralPath (Join-Path $root 'scripts\release-github.ps1') -Destination (Join-Path $fixture 'scripts')
    Copy-Item -LiteralPath (Join-Path $root 'src\AssemblyInfo.cs') -Destination (Join-Path $fixture 'src')
    $version = [Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $BinaryPath).Path).Version.ToString(3)
    $tag = 'v' + $version
    [IO.File]::WriteAllText((Join-Path $fixture ('releases\' + $tag + '.md')), 'Release notes')
    Copy-Item -LiteralPath $BinaryPath -Destination (Join-Path $fixture 'assets\dist\ShotInk.exe')
    [IO.File]::WriteAllText((Join-Path $fixture 'assets\ShotInk-portable.zip'), 'fixture')
    $script = Join-Path $fixture 'scripts\release-github.ps1'
    $assetRoot = Join-Path $fixture 'assets'
    $env:GITHUB_REF = 'refs/heads/main'
    $env:GITHUB_REF_NAME = 'main'

    & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current
    Assert-Release ((Calls-For create).Count -eq 1 -and (Calls-For edit).Count -eq 1) 'A new version did not create and publish a draft.'
    Assert-Release ((Calls-For create)[0] -contains '--draft') 'New release was visible before its assets were ready.'
    Assert-Release ($state.Notes.Trim() -eq 'Release notes') 'Internal publication details were appended to the user-facing release notes.'

    $state.Calls = @(); $state.Releases = @(@{ tag_name = $tag; draft = $false; prerelease = $false; immutable = $false })
    & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current
    Assert-Release ((Calls-For create).Count -eq 0 -and (Calls-For upload).Count -eq 1 -and (Calls-For edit).Count -eq 1) 'A small update created another release or failed to replace existing assets.'
    Assert-Release ((Calls-For upload)[0] -contains '--clobber') 'Existing asset names were not replaced.'

    $state.Calls = @(); $state.Head = 'newer'
    & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current
    Assert-Release ((Calls-For create).Count -eq 0 -and (Calls-For upload).Count -eq 0) 'An obsolete main build replaced current release assets.'

    $state.Calls = @(); $state.Head = 'current'; $state.Releases = @(@{ tag_name = 'v99.0.0'; draft = $false; prerelease = $false })
    & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current
    Assert-Release ((Calls-For create).Count -eq 0 -and (Calls-For upload).Count -eq 0) 'An older version replaced a newer release.'

    $state.Calls = @(); $state.Releases = @(@{ tag_name = $tag; immutable = $true })
    $blocked = $false
    try { & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current } catch { $blocked = $_.Exception.Message -like '*immutable*' }
    Assert-Release ($blocked -and (Calls-For upload).Count -eq 0 -and (Calls-For edit).Count -eq 1 -and $state.Notes.Trim() -eq 'Release notes') 'Immutable releases did not update their editable notes while preserving locked assets.'

    $state.Calls = @(); $state.Releases = @(@{ tag_name = $tag; immutable = $false }); $state.UploadFails = $true
    $blocked = $false
    try { & $script -AssetDirectory $assetRoot -Repository owner/repo -Commit current } catch { $blocked = $_.Exception.Message -like '*replacement failed*' }
    Assert-Release ($blocked -and (Calls-For edit).Count -eq 0) 'Failed asset upload was reported as a successful release update.'
    Write-Host 'PASS: 6 release checks'
} finally {
    $env:GITHUB_REF = $originalRef; $env:GITHUB_REF_NAME = $originalRefName
    $global:LASTEXITCODE = $originalExitCode
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Recurse -Force }
}
