$ErrorActionPreference='Stop'
$temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
$fixture=Join-Path $temporaryRoot ('ShotInk-update-smoke-'+[Guid]::NewGuid().ToString('N'))
$projectRoot=Split-Path -Parent $PSScriptRoot
$oldProcess=$null;$helper=$null
function Wait-UpdateFile([string]$Path,[int]$Seconds=20) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    while(-not(Test-Path -LiteralPath $Path)) {
        $errorFile=Join-Path (Join-Path $fixture 'stage') 'update-error.txt'
        if(Test-Path -LiteralPath $errorFile){throw ([IO.File]::ReadAllText($errorFile))}
        if($clock.Elapsed.TotalSeconds -gt $Seconds){throw ('Timed out waiting for '+[IO.Path]::GetFileName($Path))}
        Start-Sleep -Milliseconds 100
    }
}
try {
    $target=Join-Path $fixture 'installed';$stage=Join-Path $fixture 'stage';$payload=Join-Path $stage 'payload'
    New-Item -ItemType Directory -Path $target,$payload,(Join-Path $payload 'ocr'),(Join-Path $target 'captures') -Force | Out-Null
    $stop=Join-Path $fixture 'stop-old';$complete=Join-Path $fixture 'new-started'
    $oldSource='using System; using System.IO; using System.Threading; [assembly:System.Reflection.AssemblyVersion("1.0.0.0")] class Stub { static void Main(){ while(!File.Exists(@"'+$stop+'")) Thread.Sleep(100); } }'
    $newSource='using System; using System.IO; [assembly:System.Reflection.AssemblyVersion("1.8.0.0")] class Stub { static void Main(){ File.WriteAllText(@"'+$complete+'", "restarted"); } }'
    $oldFile=Join-Path $fixture 'old.cs';$newFile=Join-Path $fixture 'new.cs'
    [IO.File]::WriteAllText($oldFile,$oldSource);[IO.File]::WriteAllText($newFile,$newSource)
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /target:winexe ('/out:'+(Join-Path $target 'ShotInk.exe')) $oldFile
    if($LASTEXITCODE -ne 0){throw 'Old stub compilation failed.'}
    & $compiler /nologo /target:winexe ('/out:'+(Join-Path $payload 'ShotInk.exe')) $newFile
    if($LASTEXITCODE -ne 0){throw 'New stub compilation failed.'}
    [IO.File]::WriteAllText((Join-Path $target 'settings.ini'),'save-directory=E:\file\picture')
    [IO.File]::WriteAllText((Join-Path $target 'captures\keep.png'),'user capture')
    [IO.File]::WriteAllText((Join-Path $payload 'ShotInk.build.sha256'),('A'*64))
    [IO.File]::WriteAllText((Join-Path $payload 'ocr\ShotInk.OcrHost.exe'),'fixture component')
    [IO.File]::WriteAllText((Join-Path $payload 'update-portable.ps1'),[IO.File]::ReadAllText((Join-Path $projectRoot 'scripts\update-portable.ps1')),(New-Object Text.UTF8Encoding($true)))
    $oldHash=(Get-FileHash -LiteralPath (Join-Path $target 'ShotInk.exe')).Hash
    $oldProcess=Start-Process -FilePath (Join-Path $target 'ShotInk.exe') -WorkingDirectory $target -WindowStyle Hidden -PassThru
    foreach($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')){[IO.File]::WriteAllText((Join-Path $payload $name),'browser component')}
    [IO.File]::WriteAllText((Join-Path $payload 'ShotInk.flavor'),'ocr')
    $files=@(foreach($name in @('ShotInk.exe','ShotInk.build.sha256','ShotInk.flavor','ocr/ShotInk.OcrHost.exe','update-portable.ps1','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','WebView2Loader.dll')) { @{Path=$name;Sha256=(Get-FileHash -LiteralPath (Join-Path $payload $name)).Hash} })
    $plan=Join-Path $stage 'update-plan.json'
    @{Target=$target;Payload=$payload;Backup=(Join-Path $stage 'backup');ProcessId=$oldProcess.Id;Version='1.8.0';Files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $plan -Encoding UTF8
    $powershell=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $payload 'update-portable.ps1')+'" -Plan "'+$plan+'"'
    $helper=Start-Process -FilePath $powershell -ArgumentList $arguments -WorkingDirectory $stage -WindowStyle Hidden -PassThru
    Wait-UpdateFile (Join-Path $stage 'update-ready.txt')
    if((Get-FileHash -LiteralPath (Join-Path $target 'ShotInk.exe')).Hash -ne $oldHash){throw 'Helper replaced the executable before the old process exited.'}
    [IO.File]::WriteAllText($stop,'exit')
    Wait-UpdateFile $complete
    if(-not $helper.WaitForExit(10000) -or $helper.ExitCode -ne 0){throw 'Update helper did not complete successfully.'}
    if([IO.File]::ReadAllText((Join-Path $target 'settings.ini')) -ne 'save-directory=E:\file\picture' -or [IO.File]::ReadAllText((Join-Path $target 'captures\keep.png')) -ne 'user capture'){throw 'Real-process update changed user data.'}
    if((Get-FileHash -LiteralPath (Join-Path $stage 'backup\ShotInk.exe')).Hash -ne $oldHash){throw 'Original executable backup was lost.'}
    Write-Host 'PASS: real Windows PowerShell handshake, wait-for-exit, replacement, restart and preserved user files'
}finally {
    foreach($owned in @($helper,$oldProcess)){if($owned){try{if(-not $owned.HasExited){$owned.Kill();$owned.WaitForExit(5000) | Out-Null}}finally{$owned.Dispose()}}}
    if(Test-Path -LiteralPath $fixture){
        $resolved=[IO.Path]::GetFullPath($fixture)
        if(-not $resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'ShotInk-update-smoke-*'){throw 'Unsafe smoke-test cleanup path.'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
