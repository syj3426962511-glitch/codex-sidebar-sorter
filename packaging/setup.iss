#define ProductVersion "1.3.0"
#ifndef ProgramDirectory
  #define ProgramDirectory "..\artifacts\windows"
#endif
[Setup]
AppId={{57DA4787-4BE0-43CB-9BFC-FE83679E370F}
AppName=Codex Sidebar Sorter
AppVersion={#ProductVersion}
AppPublisher=syj3426962511-glitch
AppPublisherURL=https://github.com/syj3426962511-glitch/codex-sidebar-sorter
SetupIconFile=..\src\Assets\sidebar-sorter.ico
WizardSmallImageFile=..\src\Assets\sidebar-sorter-logo-256.png
DefaultDirName={autopf}\CodexSidebarSorter
DefaultGroupName=Codex Sidebar Sorter
DisableProgramGroupPage=yes
DisableDirPage=no
DisableWelcomePage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=CodexSidebarSorter-{#ProductVersion}-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\聊天排序工具.exe
CloseApplications=force
CloseApplicationsFilter=聊天排序工具.exe,聊天排序后台.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"

[LangOptions]
english.DialogFontName=Times New Roman
english.DialogFontSize=11
chinesesimp.DialogFontName=宋体
chinesesimp.DialogFontSize=11

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
english.OpenApplication=Open Codex Sidebar Sorter
english.ExitBeforeUninstall=The sorting operation is still finishing. Retry uninstall after it completes.
chinesesimp.DesktopShortcut=创建桌面快捷方式
chinesesimp.OpenApplication=打开聊天排序工具
chinesesimp.ExitBeforeUninstall=排序操作正在收尾，请完成后再次卸载。

[Dirs]
Name: "{app}"; Permissions: everyone-full

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: checkedonce

[Files]
Source: "{#ProgramDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\*"; DestDir: "{app}\Source"; Excludes: "bin\*,obj\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Codex Sidebar Sorter"; Filename: "{app}\聊天排序工具.exe"; Tasks: desktopicon
Name: "{autoprograms}\Codex Sidebar Sorter"; Filename: "{app}\聊天排序工具.exe"

[Run]
Filename: "{app}\聊天排序工具.exe"; Description: "{cm:OpenApplication}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeUninstall(): Boolean;
var
  BackgroundPath: String;
  ExitCode: Integer;
begin
  BackgroundPath := ExpandConstant('{app}\聊天排序后台.exe');
  Result := True;
  if FileExists(BackgroundPath) then
  begin
    Result := Exec(BackgroundPath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Result then
      Result := ExitCode = 0;
    if not Result then
      MsgBox(CustomMessage('ExitBeforeUninstall'), mbError, MB_OK);
  end;
end;
