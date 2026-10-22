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
$exe = Join-Path $AssetDirectory 'dist\ShotInk.exe'
$zip = Join-Path $AssetDirectory 'ShotInk-portable.zip'
foreach ($asset in @($exe, $zip)) { if (-not (Test-Path -LiteralPath $asset)) { throw ('Missing release asset: ' + $asset) } }
if ([Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $exe).Path).Version.ToString(3) -ne $version) { throw 'Application binary and release version do not match.' }

# An older build may finish after a newer commit. It must not replace that build.
if ($env:GITHUB_REF -eq 'refs/heads/main') {
    $head = & gh api ('repos/' + $Repository + '/git/ref/heads/main') --jq '.object.sha'
    if ($LASTEXITCODE -ne 0) { throw 'Cannot verify the current main branch.' }
    if ($head.Trim() -ne $Commit) { Write-Host 'A newer main commit exists; skipping stale release assets.'; return }
}
$allJson = & gh api ('repos/' + $Repository + '/releases?per_page=100')
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect existing releases.' }
$releases = @($allJson | ConvertFrom-Json)
$latest = $releases | Where-Object { -not $_.draft -and -not $_.prerelease -and $_.tag_name -match '^v\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.tag_name.Substring(1) } -Descending | Select-Object -First 1
if ($latest -and [version]$latest.tag_name.Substring(1) -gt [version]$version) { Write-Host 'A newer version already exists; skipping old-version publication.'; return }
$existing = $releases | Where-Object { $_.tag_name -eq $tag } | Select-Object -First 1
if ($existing -and $existing.immutable) { throw 'The existing release is immutable; its assets cannot be replaced.' }
$body = Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-release-' + [Guid]::NewGuid().ToString('N') + '.md')
try {
    $source = "`n`n安装包源码：[$Commit](https://github.com/$Repository/commit/$Commit)。`n"
    [IO.File]::WriteAllText($body, ([IO.File]::ReadAllText($notes).TrimEnd() + $source), (New-Object Text.UTF8Encoding($false)))
    if ($existing) {
        & gh release upload $tag $exe $zip --repo $Repository --clobber
        if ($LASTEXITCODE -ne 0) { throw 'Release asset replacement failed.' }
        & gh release edit $tag --repo $Repository --title $version --notes-file $body --draft=false --latest
        if ($LASTEXITCODE -ne 0) { throw 'Release notes update failed.' }
        Write-Host ('Replaced assets for ' + $tag + '; existing tag preserved.')
    } else {
        # Keep incomplete uploads out of the public release list.
        & gh release create $tag $exe $zip --repo $Repository --target $Commit --title $version --notes-file $body --draft
        if ($LASTEXITCODE -ne 0) { throw 'New draft release creation failed.' }
        & gh release edit $tag --repo $Repository --draft=false --latest
        if ($LASTEXITCODE -ne 0) { throw 'New release publication failed.' }
        Write-Host ('Published ' + $tag)
    }
} finally { if (Test-Path -LiteralPath $body) { Remove-Item -LiteralPath $body -Force } }
