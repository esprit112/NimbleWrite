; NimbleWrite for Windows: installer script for Inno Setup 6.
; Build it with  npm run package:win  (scripts/package-win.mjs stages everything in build\stage first and passes
; /DAppVersion and /DStageDir). Setup asks how to install: just for you (the default, no admin prompt) in
; %LOCALAPPDATA%\Programs\NimbleWrite, or for everyone on the PC (admin prompt) in Program Files. Upgrades keep the
; mode and folder they were installed with. /CURRENTUSER or /ALLUSERS picks the mode without asking.
; Nothing from .env is ever packaged: each person adds their own NVIDIA API key after installing.

#ifndef AppVersion
  #define AppVersion "1.0.2"
#endif
#ifndef StageDir
  #define StageDir "..\build\stage"
#endif
#define AppName "NimbleWrite"
#define AppExe "NimbleWrite.exe"
#define AppMutex "Local\NimbleWrite.Desktop"
#define QuitSignal "Local\NimbleWrite.Desktop.Quit"

[Setup]
; The AppId identifies NimbleWrite for upgrades and uninstalling: never change it.
AppId={{1BFF4C38-7EFB-4228-99A8-6A3974281647}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=NimbleWrite
AppComments=AI writing suggestions in every app, powered by NVIDIA NIM
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
; Inno Setup 6 hides the Welcome page by default; it carries the "bring your own NVIDIA key" note and the artwork.
DisableWelcomePage=no
; Per user by default; "Install for all users" restarts Setup as administrator and installs in Program Files. Picking
; Program Files without that used to fail with "access denied", so the folder page now checks first (see [Code]).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
; An upgrade reuses the mode of the copy already installed and skips the question (and the folder page).
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputBaseFilename=NimbleWrite-Setup-{#AppVersion}
SetupIconFile=..\desktop\nimblewrite.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=assets\wizard-large-100.bmp,assets\wizard-large-125.bmp,assets\wizard-large-150.bmp,assets\wizard-large-175.bmp,assets\wizard-large-200.bmp,assets\wizard-large-225.bmp,assets\wizard-large-250.bmp
WizardSmallImageFile=assets\wizard-small-100.bmp,assets\wizard-small-125.bmp,assets\wizard-small-150.bmp,assets\wizard-small-175.bmp,assets\wizard-small-200.bmp,assets\wizard-small-225.bmp,assets\wizard-small-250.bmp
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
CloseApplications=no
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nNimbleWrite suggests spelling, grammar and clarity fixes in any app as you type. For AI suggestions it uses your own NVIDIA API key, which it asks for after installing.
PrivilegesRequiredOverrideText2=%1 can be installed just for you, which needs no administrator rights, or for everyone on this PC in the Program Files folder, which does.

[Tasks]
Name: "startup"; Description: "Start NimbleWrite when I sign in to Windows"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; The bundled server and web editor are replaced as a whole on upgrade (their file names change between builds).
Type: filesandordirs; Name: "{app}\server"
Type: filesandordirs; Name: "{app}\web"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "AI writing suggestions in every app"
Name: "{autoprograms}\{#AppName} Editor"; Filename: "{app}\{#AppExe}"; Parameters: "--editor"; Comment: "Open the NimbleWrite editor in your browser"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

; "Start when I sign in" is a per-user setting, so there is no [Registry] entry for it: in an all-users install Setup
; runs as an administrator, whose HKCU may not be the person's who ran Setup. The app records it itself instead (see
; CurStepChanged), and the uninstaller removes it (CurUninstallStepChanged).

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start NimbleWrite now"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  EVENT_MODIFY_STATE = $0002;

function OpenEventW(Access: Cardinal; Inherit: Integer; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Integer;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

{ Ask a running NimbleWrite to quit (it stops its own server too) and wait up to 15 seconds for it to go. }
function QuitRunningNimbleWrite(): Boolean;
var
  H: THandle;
  I: Integer;
begin
  Result := True;
  if not CheckForMutexes('{#AppMutex}') then Exit;
  H := OpenEventW(EVENT_MODIFY_STATE, 0, '{#QuitSignal}');
  if H <> 0 then
  begin
    SetEvent(H);
    CloseHandle(H);
  end;
  for I := 1 to 60 do
  begin
    if not CheckForMutexes('{#AppMutex}') then Exit;
    Sleep(250);
  end;
  Result := not CheckForMutexes('{#AppMutex}');
end;

{ True when Setup can create folders and files in Dir or, while Dir doesn't exist yet, in the nearest folder above it
  that does. Without administrator rights, folders such as Program Files are read-only. A probe folder is created and
  removed straight away, so nothing is left behind if the person then picks somewhere else. }
function CanInstallTo(Dir: String): Boolean;
var
  Parent, Probe: String;
begin
  Result := True;
  Dir := RemoveBackslashUnlessRoot(Dir);
  while not DirExists(Dir) do
  begin
    Parent := RemoveBackslashUnlessRoot(ExtractFileDir(Dir));
    { No existing folder at all (a drive that isn't there): leave that to Setup's own checks. }
    if (Parent = '') or (Parent = Dir) then Exit;
    Dir := Parent;
  end;
  Probe := AddBackslash(Dir) + '~nimblewrite-setup-check-' + GetDateTimeString('hhnnsszzz', #0, #0);
  Result := CreateDir(Probe);
  if Result then
  begin
    Result := SaveStringToFile(Probe + '\check.tmp', 'NimbleWrite Setup', False);
    DeleteFile(Probe + '\check.tmp');
    RemoveDir(Probe);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir, Suggested: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then Exit;
  Dir := WizardDirValue;
  if CanInstallTo(Dir) then Exit;
  Result := False;
  Log('The folder page was refused: Setup cannot write to ' + Dir);
  if IsAdminInstallMode then
  begin
    SuppressibleMsgBox('Setup can''t create files in this folder:' + #13#10 + Dir + #13#10#13#10 +
      'Please choose a different folder.', mbError, MB_OK, IDOK);
    Exit;
  end;
  Suggested := ExpandConstant('{autopf}\{#AppName}');
  if SuppressibleMsgBox('You need administrator rights to install in this folder:' + #13#10 + Dir + #13#10#13#10 +
      'To install NimbleWrite there for everyone on this PC, close Setup, run it again and choose ' +
      '"Install for all users".' + #13#10#13#10 +
      'Or install it just for you, in your own folder:' + #13#10 + Suggested + #13#10#13#10 +
      'Use that folder instead?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
    WizardForm.DirEdit.Text := Suggested;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  { The folder page checks this too, but it is skipped on upgrades and in silent installs given /DIR=. }
  if not CanInstallTo(WizardDirValue) then
  begin
    Result := 'Setup can''t create files in ' + WizardDirValue + '.';
    if not IsAdminInstallMode then
      Result := Result + ' Installing there needs administrator rights: run Setup again and choose "Install for all users", or choose a folder of your own.';
    Log(Result);
    Exit;
  end;
  if not QuitRunningNimbleWrite() then
    Result := 'NimbleWrite is still running. Right-click its icon in the notification area, choose Quit NimbleWrite, then click Back and Next to try again.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep <> ssPostInstall) or not WizardIsTaskSelected('startup') then Exit;
  { Run as the person who ran Setup (not the administrator an all-users install runs as), so the setting lands in
    their own sign-in items, exactly where the tray menu's "Start with Windows" puts it. }
  if not ExecAsOriginalUser(ExpandConstant('{app}\{#AppExe}'), '--start-with-windows on', '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then
    Log(Format('Could not switch on "start with Windows" (code %d)', [Code]));
end;

function InitializeUninstall(): Boolean;
begin
  Result := QuitRunningNimbleWrite();
  if not Result then
    MsgBox('NimbleWrite is still running. Right-click its icon in the notification area, choose Quit NimbleWrite, then run the uninstaller again.', mbError, MB_OK);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  { "Start with Windows" (from Setup or the tray menu): remove it if it points at this copy. An all-users uninstall
    runs as administrator, so this reaches the sign-in items of whoever approved it, normally the same person. }
  if RegQueryStringValue(HKCU, RunKey, '{#AppName}', Value) and (Pos(Lowercase(ExpandConstant('{app}')), Lowercase(Value)) > 0) then
    RegDeleteValue(HKCU, RunKey, '{#AppName}');
  if UninstallSilent then Exit;
  if MsgBox('Also delete your NimbleWrite documents, settings and saved NVIDIA API key?' + #13#10#13#10 +
            'Choose No to keep them for next time.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
  begin
    DelTree(ExpandConstant('{userappdata}\NimbleWrite'), True, True, True);
    DelTree(ExpandConstant('{localappdata}\NimbleWrite'), True, True, True);
  end;
end;
