param([string]$BinaryPath=(Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\ShotInk.exe'))
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('ShotInk-update-helper-'+[Guid]::NewGuid().ToString('N'))
$projectRoot=Split-Path -Parent $PSScriptRoot
$state=[pscustomobject]@{Starts=0;FailOnce=$false;Failed=$false;Target=''}
function Start-Process { param($FilePath,$WorkingDirectory,$WindowStyle) $state.Starts++; if(-not(Test-Path -LiteralPath $FilePath)){throw 'Restart target missing.'} }
function Copy-Item {
    param($LiteralPath,$Destination,[switch]$Force)
    if($state.FailOnce -and -not $state.Failed -and $Destination -eq (Join-Path $state.Target 'ocr\ShotInk.OcrHost.exe')){$state.Failed=$true;throw 'Simulated locked OCR file'}
    Microsoft.PowerShell.Management\Copy-Item -LiteralPath $LiteralPath -Destination $Destination -Force:$Force
}
function Assert-Update([bool]$condition,[string]$message){if(-not $condition){throw $message}}
try {
    foreach($fail in @($false,$true)) {
        $case=Join-Path $fixture ([Guid]::NewGuid().ToString('N'));$stage=Join-Path $case 'stage';$payload=Join-Path $stage 'payload';$target=Join-Path $case 'installed'
        New-Item -ItemType Directory -Path $payload,$target,(Join-Path $payload 'ocr'),(Join-Path $target 'ocr'),(Join-Path $target 'captures') -Force | Out-Null
        [IO.File]::Copy((Resolve-Path -LiteralPath $BinaryPath).Path,(Join-Path $payload 'ShotInk.exe'))
        foreach($name in @('ShotInk.build.sha256','update-portable.ps1','ocr/ShotInk.OcrHost.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){[IO.File]::WriteAllText((Join-Path $payload $name),'new content')}
        [IO.File]::WriteAllText((Join-Path $target 'ShotInk.exe'),'old executable')
        [IO.File]::WriteAllText((Join-Path $target 'ocr\ShotInk.OcrHost.exe'),'old OCR')
        [IO.File]::WriteAllText((Join-Path $target 'settings.ini'),'save-directory=E:\file\picture')
        [IO.File]::WriteAllText((Join-Path $target 'captures\history.png'),'user image')
        [IO.File]::WriteAllText((Join-Path $payload 'ShotInk.flavor'),'ocr')
        $files=@(foreach($name in @('ShotInk.exe','ShotInk.build.sha256','ShotInk.flavor','update-portable.ps1','ocr/ShotInk.OcrHost.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){ @{Path=$name;Sha256=(Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash} })
        $planPath=Join-Path $stage 'update-plan.json'
        @{Target=$target;Payload=$payload;Backup=(Join-Path $stage 'backup');ProcessId=[int]::MaxValue;Version=[Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $BinaryPath).Path).Version.ToString(3);Files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $planPath -Encoding UTF8
        . (Join-Path $projectRoot 'scripts\update-portable.ps1') -Plan $planPath
        $state.Starts=0;$state.FailOnce=$fail;$state.Failed=$false;$state.Target=$target
        [IO.File]::WriteAllText((Join-Path $stage 'ShotInk-portable.zip'),'downloaded package')
        $failed=$false
        try { Install-PortableUpdate } catch { $failed=$true; if(-not $fail){throw} }
        Assert-Update ($failed -eq $fail) 'Install failure behavior was incorrect.'
        if($fail){
            Assert-Update ([IO.File]::ReadAllText((Join-Path $target 'ShotInk.exe')) -eq 'old executable') 'Rollback failed to restore the old executable.'
            Assert-Update ([IO.File]::ReadAllText((Join-Path $target 'ocr\ShotInk.OcrHost.exe')) -eq 'old OCR') 'Rollback damaged an unchanged locked file.'
            Assert-Update (-not(Test-Path -LiteralPath (Join-Path $target 'ShotInk.build.sha256'))) 'Rollback left a newly added file behind.'
            Assert-Update ($state.Starts -eq 0) 'Failed update restarted during rollback.'
        }else{
            Assert-Update ((Get-FileHash -LiteralPath (Join-Path $target 'ShotInk.exe')).Hash -eq (Get-FileHash -LiteralPath $BinaryPath).Hash) 'Application replacement failed.'
            Assert-Update ($state.Starts -eq 1) 'Successful update did not restart.'
        }
        Assert-Update ([IO.File]::ReadAllText((Join-Path $target 'settings.ini')) -eq 'save-directory=E:\file\picture') 'Update overwrote user settings.'
        Assert-Update ([IO.File]::ReadAllText((Join-Path $target 'captures\history.png')) -eq 'user image') 'Update overwrote image history.'
        [IO.File]::WriteAllText((Join-Path $stage 'update-error.txt'),'keep diagnostic')
        Remove-UpdatePackage
        Assert-Update (-not(Test-Path -LiteralPath (Join-Path $stage 'ShotInk-portable.zip')) -and -not(Test-Path -LiteralPath $payload)) 'Completed or failed installation retained package files.'
        Assert-Update ((Test-Path -LiteralPath (Join-Path $stage 'backup/ShotInk.exe')) -and (Test-Path -LiteralPath (Join-Path $stage 'update-error.txt'))) 'Cleanup removed rollback backup or diagnostic log.'
        $rejected=$false;try { Resolve-UpdateFile $target '../escape' | Out-Null } catch {$rejected=$true}
        Assert-Update $rejected 'Helper accepted a traversal path.'
    }
    Write-Host 'PASS: update installation, backup, rollback, restart and user-data preservation'
}finally{if(Test-Path -LiteralPath $fixture){Remove-Item -LiteralPath $fixture -Recurse -Force}}
