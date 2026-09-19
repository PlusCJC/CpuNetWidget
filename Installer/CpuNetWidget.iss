#define MyAppId "{{9A27E520-6C14-43E6-91E7-8C604DCA81BE}"
#define MyAppName "CPU 网速悬浮窗"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "CpuNetWidget"
#define MyAppExeName "CpuNetWidget.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\CpuNetWidget
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\setup
OutputBaseFilename=CpuNetWidget-Setup
SetupIconFile=..\CpuNetWidget\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
ChangesAssociations=no

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "autostart"; Description: "登录 Windows 后自动启动"; GroupDescription: "附加任务："; Flags: unchecked

[Files]
Source: "..\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\卸载 {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CpuNetWidget"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 诊断日志和其他用户级运行数据都位于此精确目录。
Type: filesandordirs; Name: "{localappdata}\CpuNetWidget"

[Code]
procedure StopRunningApplication;
var
  ResultCode: Integer;
begin
  { taskkill 找不到进程时会返回非零值；卸载仍可继续。 }
  Exec(ExpandConstant('{sys}\taskkill.exe'),
    '/F /IM "{#MyAppExeName}"', '', SW_HIDE,
    ewWaitUntilTerminated, ResultCode);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    StopRunningApplication;

    { 无论自启动是由安装器还是应用设置创建，都在卸载时移除。 }
    RegDeleteValue(HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'CpuNetWidget');

    { 清理监控选项、窗口状态和权限选项。 }
    RegDeleteKeyIncludingSubkeys(HKCU, 'Software\CpuNetWidget');
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    { 再次清理日志目录，覆盖卸载开始后产生的最后一条日志。 }
    DelTree(ExpandConstant('{localappdata}\CpuNetWidget'),
      True, True, True);
  end;
end;
