[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssetDirectory,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Commit = $env:GITHUB_SHA
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $Repository -or -not $Commit) { throw 'Specify Repository and Commit, or run this script in GitHub Actions.' }
$metadata = Get-Content -LiteralPath (Join-Path $projectRoot 'src\AssemblyInfo.cs') -Raw
if ($metadata -notmatch 'AssemblyInformationalVersion\("(\d+\.\d+\.\d+)"\)') { throw 'Missing application version.' }
$version = $Matches[1]
$tag = 'v' + $version
if ($env:GITHUB_REF -like 'refs/tags/*' -and $env:GITHUB_REF_NAME -ne $tag) { throw 'Tag and application version do not match.' }
$notes = Join-Path $projectRoot ('releases\' + $tag + '.md')
if (-not (Test-Path -LiteralPath $notes)) { throw ('Missing release notes: ' + $notes) }
$zip = Join-Path $AssetDirectory 'ShotInk-portable.zip'
if (-not (Test-Path -LiteralPath $zip)) { throw ('Missing release asset: ' + $zip) }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$probe = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-package-version-' + [Guid]::NewGuid().ToString('N') + '.exe')
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $zip).Path)
try {
    $entry = $archive.GetEntry('ShotInk.exe')
    if (-not $entry) { throw 'Portable ZIP is missing ShotInk.exe.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $probe)
    if ([Reflection.AssemblyName]::GetAssemblyName($probe).Version.ToString(3) -ne $version) { throw 'Application binary and release version do not match.' }
} finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
}

# An older build may finish after a newer commit. It must not replace that build.
if ($env:GITHUB_REF -eq 'refs/heads/main') {
    $head = & gh api ('repos/' + $Repository + '/git/ref/heads/main') --jq '.object.sha'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the current main branch.' }
    if ($head.Trim() -ne $Commit) { Write-Host 'A newer main commit exists; skipping stale release assets.'; return }
}
$allJson = & gh api ('repos/' + $Repository + '/releases?per_page=100')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing releases.' }
# Windows PowerShell returns a root JSON array as one pipeline item. Iterate
# explicitly so filtering and immutable checks always see individual releases.
$releases = @()
foreach ($release in (ConvertFrom-Json -InputObject ($allJson -join "`n"))) { $releases += $release }
$latest = $releases | Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.tag_name.Substring(1) } -Descending | Select-Object -First 1
if ($latest -and [version]$latest.tag_name.Substring(1) -gt [version]$version) { Write-Host 'A newer version already exists; skipping old-version publication.'; return }
$existing = $releases | Where-Object { $_.tag_name -eq $tag } | Select-Object -First 1
$body = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-release-' + [Guid]::NewGuid().ToString('N') + '.md')
try {
    [IO.File]::WriteAllText($body, ([IO.File]::ReadAllText($notes).TrimEnd() + "`n"), (New-Object Text.UTF8Encoding($false)))
    if ($existing) {
        if ($existing.immutable) {
            # GitHub permits editing notes on immutable releases, but locks assets.
            & gh release edit $tag --repo $Repository --title $version --notes-file $body
            if ($LASTEXITCODE -ne 0) { throw 'Release notes update failed.' }
            throw 'Release notes updated. GitHub has made this release immutable; its existing assets cannot be replaced.'
        }
        & gh release upload $tag $zip --repo $Repository --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Release asset replacement failed.' }
        & gh release edit $tag --repo $Repository --title $version --notes-file $body --draft=false --latest
        if ($LASTEXITCODE -ne 0) { throw 'Release notes update failed.' }
        foreach ($legacyName in @('ShotInk.exe', 'ShotInk-Setup.exe')) {
            if ($existing.assets | Where-Object { $_.name -eq $legacyName }) {
                & gh release delete-asset $tag $legacyName --repo $Repository --yes
                if ($LASTEXITCODE -ne 0) { throw ('Cannot remove the superseded release asset: ' + $legacyName) }
            }
        }
        Write-Host ('Replaced assets for ' + $tag + '; existing tag preserved.')
    } else {
        # Keep incomplete uploads out of the public release list.
        & gh release create $tag $zip --repo $Repository --target $Commit --title $version --notes-file $body --draft
        if ($LASTEXITCODE -ne 0) { throw 'New draft release creation failed.' }
        & gh release edit $tag --repo $Repository --draft=false --latest
        if ($LASTEXITCODE -ne 0) { throw 'New release publication failed.' }
        Write-Host ('Published ' + $tag)
    }
} finally { if (Test-Path -LiteralPath $body) { Remove-Item -LiteralPath $body -Force } }
