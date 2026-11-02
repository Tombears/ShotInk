param([Parameter(Mandatory=$true)][string]$Plan)
$ErrorActionPreference='Stop'
$planPath=[IO.Path]::GetFullPath($Plan)
$updateRoot=Split-Path -Parent $planPath
$update=Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
$script:CanRestart=$true
function Resolve-UpdateFile([string]$Root,[string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative.Contains('\') -or $Relative.Contains(':')) { throw 'Invalid update file path.' }
    foreach($part in $Relative.Split('/')) { if (-not $part -or $part -eq '..' -or $part -eq '.' -or $part.TrimEnd(' ','.') -ne $part -or $part -match '[<>"|?*\x00-\x1f]|^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\.)') { throw 'Invalid update path segment.' } }
    $prefix=[IO.Path]::GetFullPath($Root).TrimEnd('\')+'\'
    $file=[IO.Path]::GetFullPath((Join-Path $Root $Relative.Replace('/','\')))
    if(-not $file.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Update path escaped its root.'}
    $parent=$file
    while($parent -and $parent.Length -ge $prefix.Length-1) {
        if((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Update cannot follow directory links.'}
        $parent=Split-Path -Parent $parent
    }
    return $file
}
function Get-UpdateHash([string]$Path) {
    $algorithm=[Security.Cryptography.SHA256]::Create();$stream=[IO.File]::OpenRead($Path)
    try{return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')}
    finally{$stream.Dispose();$algorithm.Dispose()}
}
function Install-PortableUpdate {
    $payload=[IO.Path]::GetFullPath($update.Payload)
    $backup=[IO.Path]::GetFullPath($update.Backup)
    if($payload -ne (Join-Path $updateRoot 'payload') -or $backup -ne (Join-Path $updateRoot 'backup')){throw 'Invalid update staging directories.'}
    $target=[IO.Path]::GetFullPath($update.Target)
    if($target.StartsWith($updateRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $updateRoot.StartsWith($target.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Staging and installation directories must be separate.'}
    if(-not(Test-Path -LiteralPath (Join-Path $target 'ShotInk.exe'))){throw 'ShotInk installation was not found.'}
    $files=@($update.Files)
    if($files.Count -eq 0 -or $files.Count -gt 6000){throw 'Invalid update file count.'}
    $seen=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach($file in $files) {
        if(-not $seen.Add([string]$file.Path) -or $file.Path -match '(?i)settings\.ini|^(captures|updates)/'){throw 'Update cannot overwrite user data or duplicate files.'}
        $source=Resolve-UpdateFile $payload $file.Path
        $null=Resolve-UpdateFile $target $file.Path
        if((Get-UpdateHash $source) -ne $file.Sha256){throw 'Staged update checksum failed.'}
    }
    foreach($required in @('ShotInk.exe','ShotInk.build.sha256','ocr/ShotInk.OcrHost.exe','update-portable.ps1')){if(-not $seen.Contains($required)){throw 'Update omitted a required file.'}}
    if([Reflection.AssemblyName]::GetAssemblyName((Join-Path $payload 'ShotInk.exe')).Version.ToString(3) -ne $update.Version){throw 'Update version mismatch.'}
    [IO.File]::WriteAllText((Join-Path $updateRoot 'update-ready.txt'),'ready')
    $process=Get-Process -Id $update.ProcessId -ErrorAction SilentlyContinue
    if($process -and -not $process.WaitForExit(120000)){throw 'ShotInk did not exit. No program files were changed.'}
    # OCR and antivirus may release their file handles slightly after the UI exits.
    Start-Sleep -Milliseconds 750
    $changed=New-Object 'System.Collections.Generic.List[object]'
    try {
        foreach($file in $files) {
            $source=Resolve-UpdateFile $payload $file.Path
            $destination=Resolve-UpdateFile $target $file.Path
            $saved=Resolve-UpdateFile $backup $file.Path
            $existed=Test-Path -LiteralPath $destination -PathType Leaf
            if($existed){New-Item -ItemType Directory -Path (Split-Path -Parent $saved) -Force | Out-Null; Copy-Item -LiteralPath $destination -Destination $saved}
            $changed.Add([pscustomobject]@{Path=$file.Path;Existed=$existed})
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $destination -Force
            if((Get-UpdateHash $destination) -ne $file.Sha256){throw 'Installed file checksum failed.'}
        }
    } catch {
        $failure=$_.Exception.Message
        $rollbackErrors=New-Object 'System.Collections.Generic.List[string]'
        for($index=$changed.Count-1;$index -ge 0;$index--) {
            try {
                $entry=$changed[$index];$destination=Resolve-UpdateFile $target $entry.Path
                if($entry.Existed){
                    $saved=Resolve-UpdateFile $backup $entry.Path
                    if(-not(Test-Path -LiteralPath $destination) -or (Get-UpdateHash $destination) -ne (Get-UpdateHash $saved)){Copy-Item -LiteralPath $saved -Destination $destination -Force}
                }
                elseif(Test-Path -LiteralPath $destination -PathType Leaf){Remove-Item -LiteralPath $destination -Force}
            } catch { $rollbackErrors.Add($_.Exception.Message) }
        }
        if($rollbackErrors.Count -gt 0){$script:CanRestart=$false;throw ('更新失败，部分文件未能恢复。请保留备份目录并手动恢复：'+$backup)}
        throw ('更新失败，已恢复原程序：'+$failure)
    }
    Start-Process -FilePath (Join-Path $target 'ShotInk.exe') -WorkingDirectory $target -WindowStyle Hidden
}
# Tests dot-source the functions without installing or restarting a real application.
if($MyInvocation.InvocationName -ne '.') {
    try { Install-PortableUpdate }
    catch {
        $message=$_.Exception.Message
        Set-Content -LiteralPath (Join-Path $updateRoot 'update-error.txt') -Value $message -Encoding UTF8
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show($message+"`n`n更新日志："+$updateRoot,'ShotInk 更新未完成') | Out-Null
        if($script:CanRestart -and (Test-Path -LiteralPath (Join-Path $update.Target 'ShotInk.exe'))) { Start-Process -FilePath (Join-Path $update.Target 'ShotInk.exe') -WorkingDirectory $update.Target -WindowStyle Hidden }
        exit 1
    }
}
