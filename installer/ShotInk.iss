#ifndef SourceRoot
  #define SourceRoot ".."
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif

[Setup]
AppId={{C4996033-2445-4AF4-9497-F490EFD46448}
AppName=ShotInk
AppVersion={#AppVersion}
AppVerName=ShotInk {#AppVersion}
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\ShotInk
DefaultGroupName=ShotInk
DisableProgramGroupPage=yes
OutputDir={#SourceRoot}
OutputBaseFilename=ShotInk-Setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
PrivilegesRequired=admin
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\ShotInk.exe
LicenseFile={#SourceRoot}\LICENSE
SetupLogging=yes

[Languages]
Name: "chinesesimplified"; MessagesFile: "{#SourceRoot}\installer\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceRoot}\build\redist\NDP48-x86-x64-AllOS-ENU.exe"; Flags: dontcopy
Source: "{#SourceRoot}\build\redist\VC_redist.x64.exe"; Flags: dontcopy
Source: "{#SourceRoot}\dist\*"; DestDir: "{app}"; Excludes: "*.pdb,*.lib,ShotInk.build.sha256"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ShotInk"; Filename: "{app}\ShotInk.exe"
Name: "{commondesktop}\ShotInk"; Filename: "{app}\ShotInk.exe"

[Run]
Filename: "{app}\ShotInk.exe"; Description: "启动 ShotInk"; Flags: nowait postinstall skipifsilent runasoriginaluser; Check: CanLaunchApp

[Code]
var
  DependencyRestart: Boolean;

function NetFrameworkReady: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

function VcRuntimeReady: Boolean;
var
  Installed, Major, Minor, Build, Revision: Cardinal;
  Key: String;
begin
  Key := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  Result := False;
  if not RegQueryDWordValue(HKLM64, Key, 'Installed', Installed) then Exit;
  if Installed <> 1 then Exit;
  if not RegQueryDWordValue(HKLM64, Key, 'Major', Major) then Exit;
  if not RegQueryDWordValue(HKLM64, Key, 'Minor', Minor) then Exit;
  if not RegQueryDWordValue(HKLM64, Key, 'Bld', Build) then Exit;
  if not RegQueryDWordValue(HKLM64, Key, 'Rbld', Revision) then Exit;
  if Major <> {#VcMajor} then Result := Major > {#VcMajor}
  else if Minor <> {#VcMinor} then Result := Minor > {#VcMinor}
  else if Build <> {#VcBuild} then Result := Build > {#VcBuild}
  else Result := Revision >= {#VcRevision};
end;

function InstallDependency(FileName, Parameters: String; AllowNewer: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  ExtractTemporaryFile(FileName);
  if not Exec(ExpandConstant('{tmp}\') + FileName, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := '无法启动运行环境安装程序，请重试。'
  else if (ExitCode = 3010) or (ExitCode = 1641) then
    DependencyRestart := True
  else if (ExitCode <> 0) and not (AllowNewer and (ExitCode = 1638)) then
    Result := Format('运行环境安装失败（代码 %d），请重试。', [ExitCode]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not NetFrameworkReady then begin
    WizardForm.StatusLabel.Caption := '正在配置 .NET Framework 4.8…';
    Result := InstallDependency('NDP48-x86-x64-AllOS-ENU.exe', '/q /norestart', False);
    if Result <> '' then Exit;
  end;
  if not VcRuntimeReady then begin
    WizardForm.StatusLabel.Caption := '正在配置 Visual C++ 运行环境…';
    Result := InstallDependency('VC_redist.x64.exe', '/install /quiet /norestart', True);
  end;
end;

function NeedRestart: Boolean;
begin
  Result := DependencyRestart;
end;

function CanLaunchApp: Boolean;
begin
  Result := not DependencyRestart;
end;
