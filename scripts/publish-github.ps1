[CmdletBinding()]
param(
    [string]$Owner,
    [ValidatePattern('^[A-Za-z0-9_.-]+$')][string]$Repository = 'ShotInk',
    [ValidateSet('private', 'public')][string]$Visibility = 'private'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
foreach ($command in @('git', 'gh')) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Install $command first." } }
$login = & gh api user --jq '.login'
if ($LASTEXITCODE -ne 0) { throw 'Run gh auth login, then retry.' }
Push-Location -LiteralPath $projectRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
        & git init -b main
        if ($LASTEXITCODE -ne 0) { throw 'git init failed.' }
    }
    $gitRoot = & git rev-parse --show-toplevel
    if ($LASTEXITCODE -ne 0 -or [IO.Path]::GetFullPath($gitRoot.Trim()) -ne [IO.Path]::GetFullPath($projectRoot)) { throw 'Unexpected Git repository root.' }
    $remote = $null
    $remotes = @(& git remote)
    if ($remotes -contains 'origin') { $remote = & git remote get-url origin }
    # Existing checkouts keep using their current repository across a product rename.
    if ($remote -and $remote.Trim() -match '^(?:https://github\.com/|git@github\.com:)([A-Za-z0-9-]+)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$') {
        if (-not $Owner) { $Owner = $Matches[1] }
        if (-not $PSBoundParameters.ContainsKey('Repository')) { $Repository = $Matches[2] }
    }
    if (-not $Owner) { $Owner = $login.Trim() }
    if ($Owner -notmatch '^[A-Za-z0-9-]+$') { throw 'Invalid GitHub owner.' }
    $fullName = "$Owner/$Repository"
    $author = & git config user.name
    if (-not $author) { & git config user.name $login.Trim() }
    $email = & git config user.email
    if (-not $email) { & git config user.email ($login.Trim() + '@users.noreply.github.com') }
    & git add -- .
    if ($LASTEXITCODE -ne 0) { throw 'git add failed.' }
    & git diff --cached --quiet
    if ($LASTEXITCODE -eq 1) {
        & git commit -m 'Build Windows screenshot, annotation, pin and scrolling capture tool'
        if ($LASTEXITCODE -ne 0) { throw 'git commit failed.' }
    } elseif ($LASTEXITCODE -ne 0) { throw 'Cannot inspect staged changes.' }
    # A missing repository is expected on the first upload; Windows PowerShell 5
    # otherwise treats redirected native stderr as a terminating error.
    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $repoExists = & gh repo view $fullName --json nameWithOwner --jq '.nameWithOwner' 2>$null
        $repoViewCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $savedPreference }
    if ($repoViewCode -ne 0) {
        if ($remote) { throw 'Origin already exists, but the requested GitHub repository is unavailable. Check it before retrying.' }
        & gh repo create $fullName "--$Visibility" --description 'Windows screenshot, annotation, desktop pinning and scrolling capture tool' --source $projectRoot --remote origin
        if ($LASTEXITCODE -ne 0) { throw 'GitHub repository creation failed.' }
    } else {
        $url = "git@github.com:$fullName.git"
        if ($remote -and $remote.Trim().TrimEnd('/') -ne $url -and $remote.Trim().TrimEnd('/') -ne "https://github.com/$fullName.git") { throw 'Origin points to a different repository; no remote was changed.' }
        if (-not $remote) { & git remote add origin $url; if ($LASTEXITCODE -ne 0) { throw 'Cannot add origin.' } }
    }
    & git remote set-url origin "git@github.com:$fullName.git"
    if ($LASTEXITCODE -ne 0) { throw 'Cannot configure SSH origin.' }
    & git push -u origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Push failed. Remote history was preserved; no force push was attempted.' }
    Write-Host "Uploaded: https://github.com/$fullName"
} finally { Pop-Location }
