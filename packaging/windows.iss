#ifndef ProductVersion
  #error "Pass the version from desktop/SidebarSorter.Desktop.csproj as /DProductVersion."
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
LanguageDetectionMethod=none
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=CodexSidebarSorter-{#ProductVersion}-Windows-Setup
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\SidebarSorter.exe
CloseApplications=yes
CloseApplicationsFilter=SidebarSorter.exe
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "ChineseSimplified.isl"
Name: "chinesetrad"; MessagesFile: "Languages\ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "Languages\Japanese.isl"
Name: "korean"; MessagesFile: "Languages\Korean.isl"
Name: "french"; MessagesFile: "Languages\French.isl"
Name: "german"; MessagesFile: "Languages\German.isl"
Name: "spanish"; MessagesFile: "Languages\Spanish.isl"
Name: "portuguese"; MessagesFile: "Languages\BrazilianPortuguese.isl"
Name: "italian"; MessagesFile: "Languages\Italian.isl"
Name: "russian"; MessagesFile: "Languages\Russian.isl"
Name: "arabic"; MessagesFile: "Languages\Arabic.isl"

[LangOptions]
english.DialogFontName=Times New Roman
english.DialogFontSize=11
chinesesimp.DialogFontName=宋体
chinesesimp.DialogFontSize=11
chinesetrad.DialogFontName=宋体
chinesetrad.DialogFontSize=11

[CustomMessages]
english.DesktopShortcut=Create a desktop shortcut
english.OpenApplication=Open Codex Sidebar Sorter
english.ExitBeforeUpdate=The sorting operation is still finishing. Retry after it completes.
chinesesimp.DesktopShortcut=创建桌面快捷方式
chinesesimp.OpenApplication=打开聊天排序工具
chinesesimp.ExitBeforeUpdate=排序操作正在收尾，请完成后重试。
chinesetrad.DesktopShortcut=建立桌面捷徑
chinesetrad.OpenApplication=開啟聊天排序工具
chinesetrad.ExitBeforeUpdate=排序操作正在完成，請稍後重試。
japanese.DesktopShortcut=デスクトップにショートカットを作成
japanese.OpenApplication=チャット並べ替えを開く
japanese.ExitBeforeUpdate=並べ替え処理の完了後に再試行してください。
korean.DesktopShortcut=바탕 화면 바로 가기 만들기
korean.OpenApplication=채팅 정렬 도구 열기
korean.ExitBeforeUpdate=정렬 작업이 완료된 후 다시 시도하세요.
french.DesktopShortcut=Créer un raccourci sur le bureau
french.OpenApplication=Ouvrir le tri des discussions
french.ExitBeforeUpdate=Réessayez lorsque le tri est terminé.
german.DesktopShortcut=Desktop-Verknüpfung erstellen
german.OpenApplication=Chat-Sortierung öffnen
german.ExitBeforeUpdate=Nach Abschluss der Sortierung erneut versuchen.
spanish.DesktopShortcut=Crear un acceso directo en el escritorio
spanish.OpenApplication=Abrir ordenación de chats
spanish.ExitBeforeUpdate=Vuelve a intentarlo cuando termine la ordenación.
portuguese.DesktopShortcut=Criar atalho na área de trabalho
portuguese.OpenApplication=Abrir ordenação de conversas
portuguese.ExitBeforeUpdate=Tente novamente quando a ordenação terminar.
italian.DesktopShortcut=Crea un collegamento sul desktop
italian.OpenApplication=Apri ordinamento chat
italian.ExitBeforeUpdate=Riprova al termine dell’ordinamento.
russian.DesktopShortcut=Создать ярлык на рабочем столе
russian.OpenApplication=Открыть сортировку чатов
russian.ExitBeforeUpdate=Повторите попытку после завершения сортировки.
arabic.DesktopShortcut=إنشاء اختصار على سطح المكتب
arabic.OpenApplication=فتح أداة ترتيب المحادثات
arabic.ExitBeforeUpdate=أعد المحاولة بعد اكتمال الترتيب.

[Dirs]
Name: "{app}"

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; Flags: checkedonce

[Files]
Source: "..\artifacts\windows\*"; DestDir: "{app}"; Excludes: "*.obj,*.lib,*.exp,*.pdb,build-tray.cmd"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}\Source"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}\Source"; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}\Source"; Flags: ignoreversion
Source: "..\docs\*"; DestDir: "{app}\Source\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\.github\workflows\*"; DestDir: "{app}\Source\.github\workflows"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\desktop\*"; DestDir: "{app}\Source\desktop"; Excludes: "bin\*,obj\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\src\*"; DestDir: "{app}\Source\src"; Excludes: "bin\*,obj\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\tests\*"; DestDir: "{app}\Source\tests"; Excludes: "bin\*,obj\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\packaging\*"; DestDir: "{app}\Source\packaging"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Codex Sidebar Sorter"; Filename: "{app}\SidebarSorter.exe"; Tasks: desktopicon
Name: "{autoprograms}\Codex Sidebar Sorter"; Filename: "{app}\SidebarSorter.exe"

[Run]
Filename: "{app}\SidebarSorter.exe"; Description: "{cm:OpenApplication}"; Flags: nowait postinstall skipifsilent

[Code]
function PreviousUninstallerPath(): String;
var
  RegistryKey: String;
begin
  RegistryKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{57DA4787-4BE0-43CB-9BFC-FE83679E370F}_is1';
  if not RegQueryStringValue(HKCU, RegistryKey, 'UninstallString', Result) then
    if not RegQueryStringValue(HKLM, RegistryKey, 'UninstallString', Result) then
      Result := '';
  Result := RemoveQuotes(Result);
end;

function WaitForPreviousTool(InstallDirectory: String): Boolean;
var
  Locator: Variant;
  Services: Variant;
  Processes: Variant;
  PreviousProcess: Variant;
  Index: Integer;
  Attempt: Integer;
  Found: Boolean;
begin
  InstallDirectory := AddBackslash(InstallDirectory);
  Locator := CreateOleObject('WbemScripting.SWbemLocator');
  Services := Locator.ConnectServer('.', 'root\cimv2');
  Result := False;
  for Attempt := 1 to 240 do
  begin
    Found := False;
    Processes := Services.ExecQuery('SELECT ExecutablePath FROM Win32_Process WHERE Name=''' +
      #$804A#$5929#$6392#$5E8F#$5DE5#$5177 + '.exe'' OR Name=''' +
      #$804A#$5929#$6392#$5E8F#$540E#$53F0 + '.exe''');
    for Index := 0 to Processes.Count - 1 do
    begin
      PreviousProcess := Processes.ItemIndex(Index);
      if not VarIsNull(PreviousProcess.ExecutablePath) then
        if CompareText(ExtractFilePath(PreviousProcess.ExecutablePath), InstallDirectory) = 0 then
          Found := True;
    end;
    if not Found then
    begin
      Result := True;
      Exit;
    end;
    Sleep(250);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  PreviousUninstaller: String;
  PreviousBackground: String;
  PreviousDirectory: String;
  ExitCode: Integer;
begin
  Result := '';
  PreviousUninstaller := PreviousUninstallerPath();
  if PreviousUninstaller = '' then
    Exit;
  PreviousDirectory := AddBackslash(ExtractFilePath(PreviousUninstaller));
  PreviousBackground := PreviousDirectory + 'SidebarSorter.exe';
  if FileExists(PreviousBackground) and
      (CompareText(PreviousDirectory, AddBackslash(ExpandConstant('{app}'))) = 0) then
  begin
    if not Exec(PreviousBackground, '--exit', PreviousDirectory, SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := CustomMessage('ExitBeforeUpdate')
    else if ExitCode <> 0 then
      Result := CustomMessage('ExitBeforeUpdate');
    Exit;
  end;
  PreviousBackground := PreviousDirectory + #$804A#$5929#$6392#$5E8F#$540E#$53F0 + '.exe';
  if FileExists(PreviousBackground) then
  begin
    if not Exec(PreviousBackground, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := CustomMessage('ExitBeforeUpdate');
      Exit;
    end;
    if ExitCode <> 0 then
    begin
      Result := CustomMessage('ExitBeforeUpdate');
      Exit;
    end;
    if not WaitForPreviousTool(ExtractFilePath(PreviousUninstaller)) then
    begin
      Result := CustomMessage('ExitBeforeUpdate');
      Exit;
    end;
  end;
  if FileExists(PreviousUninstaller) then
  begin
    if not Exec(PreviousUninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
      Result := CustomMessage('ExitBeforeUpdate')
    else if ExitCode <> 0 then
      Result := CustomMessage('ExitBeforeUpdate');
  end;
end;

function InitializeUninstall(): Boolean;
var
  ProgramPath: String;
  ExitCode: Integer;
begin
  ProgramPath := ExpandConstant('{app}\SidebarSorter.exe');
  Result := True;
  if FileExists(ProgramPath) then
  begin
    Result := Exec(ProgramPath, '--exit', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
    if Result then
      Result := ExitCode = 0;
    if not Result then
      MsgBox(CustomMessage('ExitBeforeUpdate'), mbError, MB_OK);
  end;
end;

