#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#define ProductDir "..\artifacts\bin"
[Setup]
AppId={{E7E04577-7B22-4F35-A35E-58408344552C}
AppName=Pointing Stick 指点杆开关
AppVersion={#AppVersion}
AppPublisher=PointingStickSwitch
DefaultDirName={autopf}\AccuPointSwitch
UsePreviousAppDir=no
DefaultGroupName=Pointing Stick 指点杆开关
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableWelcomePage=no
PrivilegesRequired=admin
ArchitecturesAllowed=x86compatible or x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.15063
OutputDir=..\artifacts\release
OutputBaseFilename=PointingStickSwitch-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\TrackPointSwitch.exe
CloseApplications=yes
CloseApplicationsFilter=TrackPointSwitch.exe
RestartApplications=no
SetupLogging=yes
InfoBeforeFile=installer-info.txt
VersionInfoVersion={#AppVersion}.0
VersionInfoDescription=Pointing Stick 指点杆开关安装程序
UninstallDisplayName=Pointing Stick 指点杆开关

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[Files]
Source: "{#ProductDir}\TrackPointSwitch.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProductDir}\TrackPointSwitch.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProductDir}\SynapticsHost32.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#ProductDir}\SynapticsHost32.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\docs\compatibility.md"; DestDir: "{app}"; DestName: "兼容性与验证.md"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "installer-info.txt"; DestDir: "{app}"; DestName: "使用说明.txt"; Flags: ignoreversion

[Icons]
Name: "{commondesktop}\一键切换指点杆"; Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--toggle"; WorkingDir: "{app}"
Name: "{commondesktop}\指点杆控制面板"; Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--ui"; WorkingDir: "{app}"
Name: "{group}\指点杆控制面板"; Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--ui"
Name: "{group}\启用指点杆（恢复）"; Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--enable"
Name: "{group}\卸载 Pointing Stick 指点杆开关"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--agent"; Flags: nowait runasoriginaluser
Filename: "{app}\TrackPointSwitch.exe"; Parameters: "--ui"; Description: "打开指点杆控制面板"; Flags: postinstall nowait skipifsilent runasoriginaluser

[Code]
var
  UpgradeStopped: Boolean;
  SetupCompleted: Boolean;

function InitializeSetup(): Boolean;
var Release: Cardinal;
begin
  Release := 0;
  if IsWin64 then
    Result := RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
  else
    Result := RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  Result := Result and (Release >= 528040);
  if not Result then
    MsgBox('需要 Microsoft .NET Framework 4.8 或更高版本。请安装后重新运行此安装程序。', mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExitCode: Integer; CheckExe: String;
begin
  Result := '';
  ExtractTemporaryFile('TrackPointSwitch.exe');
  ExtractTemporaryFile('TrackPointSwitch.exe.config');
  CheckExe := ExpandConstant('{tmp}\TrackPointSwitch.exe');
  if not Exec(CheckExe, '--prepare-upgrade --quiet', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then begin
    Result := '无法暂停已有服务。请关闭指点杆程序并检查服务后重试。';
    Exit;
  end;
  UpgradeStopped := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ExitCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{app}\TrackPointSwitch.exe'), '--install --quiet', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) or (ExitCode <> 0) then
      RaiseException('后台服务安装失败，请重试安装或查看程序日志。');
    SetupCompleted := True;
  end;
end;

procedure DeinitializeSetup();
var ExitCode: Integer;
begin
  if UpgradeStopped and not SetupCompleted and FileExists(ExpandConstant('{app}\TrackPointSwitch.exe')) then
    Exec(ExpandConstant('{app}\TrackPointSwitch.exe'), '--resume-service --quiet', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;

function InitializeUninstall(): Boolean;
var ExitCode: Integer; HelperExe: String;
begin
  HelperExe := ExpandConstant('{tmp}\AccuPoint-uninstall-helper.exe');
  Result := CopyFile(ExpandConstant('{app}\TrackPointSwitch.exe'), HelperExe, False);
  if Result then Result := CopyFile(ExpandConstant('{app}\TrackPointSwitch.exe.config'), HelperExe + '.config', False);
  if Result then Result := Exec(HelperExe, '--uninstall --quiet', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0);
  if not Result then
    MsgBox('卸载失败，请检查服务和设备状态后重试。', mbError, MB_OK);
end;
