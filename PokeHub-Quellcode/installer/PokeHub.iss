; Installer for MonHub (launcher with its randomizer + melonDS + DeSmuME + mGBA + Azahar). Build with build-pokehub.ps1.
; Everything goes below ONE folder; ROMs, saves, randomized ROMs and fangames survive an uninstall.

#define AppName "MonHub"
#define AppVersion "2.5.0"
#define Root "..\.."
#define BuildDir Root + "\Hub-Build"

[Setup]
AppId={{9B6C2F4E-3E1A-4B8D-A7C2-5D0E8F1A2B77}
AppName={#AppName}
AppVersion={#AppVersion}
; the search in Settings > Apps matches name OR publisher
AppPublisher=MonHub
; earlier installs (named PokéHub) keep their folder – Setup reuses the previous one
DefaultDirName={%USERPROFILE}\MonHub
; after an uninstall the folder still holds the player's ROMs and saves – reinstalling there is the normal case
DirExistsWarning=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#Root}\Installer
OutputBaseFilename=MonHub-Setup
Compression=lzma2/ultra64
SolidCompression=yes
; the look: Windows 11 style that follows light/dark mode, no separator lines; MonHub's banner (art\make_art.py)
WizardStyle=modern dynamic windows11 hidebevels
WizardImageFile=art\wizard-164x314.bmp,art\wizard-205x393.bmp,art\wizard-246x471.bmp,art\wizard-287x550.bmp,art\wizard-328x628.bmp
WizardSmallImageFile=art\small-55x55.png,art\small-69x69.png,art\small-83x83.png,art\small-96x96.png,art\small-110x110.png
; the same pictures in dark mode (otherwise Inno shows its own dark box picture)
WizardImageFileDynamicDark=art\wizard-164x314.bmp,art\wizard-205x393.bmp,art\wizard-246x471.bmp,art\wizard-287x550.bmp,art\wizard-328x628.bmp
WizardSmallImageFileDynamicDark=art\small-55x55.png,art\small-69x69.png,art\small-83x83.png,art\small-96x96.png,art\small-110x110.png
; few clicks: welcome (with the banner), folder only on the first install, desktop icon, go
DisableWelcomePage=no
DisableDirPage=auto
DisableReadyPage=yes
SetupIconFile=..\pokeball.ico
UninstallDisplayIcon={app}\System\App\MonHub.exe
UninstallDisplayName={#AppName}

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Messages]
WelcomeLabel1=Willkommen bei MonHub!
WelcomeLabel2=Dein Spielzentrum: Randomizer, Emulatoren für Game Boy, GBA, DS und 3DS und deine Fangames – alles in einem Fenster.%n%nDie Installation dauert nur einen Moment. Deine ROMs bringst du selbst mit – MonHub hilft dir beim ersten Start, sie zu importieren.
FinishedHeadingLabel=Fertig – viel Spaß!
FinishedLabel=MonHub ist installiert.%n%nBeim ersten Start zeigt dir Porygon, wo alles ist. Du findest MonHub im Startmenü, auf dem Desktop oder über die Windows-Suche.
FinishedLabelNoIcons=MonHub ist installiert.%n%nBeim ersten Start zeigt dir Porygon, wo alles ist. Du findest MonHub über die Windows-Suche („MonHub“).
ClickFinish=Klicke auf „Fertigstellen“.
WizardSelectTasks=Desktop-Symbol
SelectTasksDesc=Fast fertig!
SelectTasksLabel2=Soll MonHub auch ein Symbol auf dem Desktop bekommen? Im Startmenü und in der Windows-Suche findest du es sowieso.
; ROMs, saves and fangames are kept on purpose – the final uninstall message says so, otherwise the folder looks like a leftover
UninstalledAll=%1 wurde entfernt.%n%nDeine ROMs, Spielstände und randomisierten Spiele sind noch da – im MonHub-Ordner (den du bei der Installation gewählt hast). Wenn du sie nicht mehr brauchst, kannst du den Ordner selbst löschen.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Dirs]
Name: "{app}\ROMs"; Flags: uninsneveruninstall
Name: "{app}\Randomisierte ROMs"; Flags: uninsneveruninstall
Name: "{app}\Spielstände"; Flags: uninsneveruninstall
Name: "{app}\Fangames"; Flags: uninsneveruninstall

[Files]
Source: "{#BuildDir}\System\*"; DestDir: "{app}\System"; Flags: ignoreversion recursesubdirs; Excludes: "melonDS.toml,*.rnqs"
; randomizer presets: a preset already there (maybe changed by the player) is kept
Source: "{#BuildDir}\System\App\Settings\*.rnqs"; DestDir: "{app}\System\App\Settings"; Flags: onlyifdoesntexist
; emulator settings only on first install – later changes by the player are kept on updates
Source: "{#BuildDir}\System\Emulatoren\melonDS\melonDS.toml"; DestDir: "{app}\System\Emulatoren\melonDS"; Flags: onlyifdoesntexist
Source: "LIESMICH.txt"; DestDir: "{app}"; Flags: ignoreversion

[INI]
; a new stamp on every install (also the same version again): MonHub's Porygon then offers the tour
Filename: "{app}\System\App\install.ini"; Section: "Setup"; Key: "Version"; String: "{#AppVersion}"
Filename: "{app}\System\App\install.ini"; Section: "Setup"; Key: "Stamp"; String: "{code:InstallStamp}"

[Icons]
; one clean Start menu entry – found by the Windows search like any installed app ("pokehub" works too)
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\System\App\MonHub.exe"; Comment: "Randomizer, Emulatoren (Game Boy, GBA, DS, 3DS) und Fangames"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\System\App\MonHub.exe"; Tasks: desktopicon

[InstallDelete]
; up to 2.4: a test preset, and the default preset under its old name (now Randomizer_Nuzlock_with_Items)
Type: files; Name: "{app}\System\App\Settings\test1.rnqs"
Type: files; Name: "{app}\System\App\Settings\Merlinstehtaufkleinkinder.rnqs"
; up to 2.4 the app was called PokéHub: its program files and shortcuts go, the player's folders stay
Type: files; Name: "{app}\System\App\PokeHub.*"
Type: files; Name: "{autoprograms}\PokéHub.lnk"
Type: files; Name: "{autodesktop}\PokéHub.lnk"
; up to 2.4 the stand-alone Poké Rando came along – it is gone, its files go on the update
Type: files; Name: "{app}\System\App\RandoApp.*"
; 1.0 had a second Start menu entry that showed up as an extra "app" in the search
Type: files; Name: "{autoprograms}\PokéHub – Liesmich.lnk"

[Run]
Filename: "{app}\System\App\MonHub.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; the long guide stays in the folder; here only on request
Filename: "{app}\LIESMICH.txt"; Description: "Anleitung (LIESMICH) öffnen"; Flags: shellexec nowait postinstall skipifsilent unchecked

[UninstallDelete]
; runtime files of the programs (configs, emulator data) – NOT the player's folders
Type: filesandordirs; Name: "{app}\System"

[Code]
{ No "ready" page: the page before the install says what the next click does. }
procedure CurPageChanged(CurPageID: Integer);
begin
  { the desktop icon page is always the last one before the install }
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

{ Time of this install, written to install.ini – see [INI]. }
function InstallStamp(Param: String): String;
begin
  Result := GetDateTimeString('yyyymmddhhnnss', #0, #0);
end;

{ Programs still running from the MonHub folder (launcher, emulators, Java), one per line. }
function RunningFromApp(): String;
var
  Locator, Service, Processes, Process: Variant;
  I: Integer;
  AppDir, ExePath, ExeName: String;
begin
  Result := '';
  AppDir := Lowercase(AddBackslash(ExpandConstant('{app}')));
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name, ExecutablePath FROM Win32_Process');
    for I := 0 to Processes.Count - 1 do
    begin
      Process := Processes.ItemIndex(I);
      if not VarIsNull(Process.ExecutablePath) then
      begin
        ExePath := Process.ExecutablePath;
        ExeName := Process.Name;
        { unins000.exe is this uninstaller itself }
        if (Pos(AppDir, Lowercase(ExePath)) = 1) and (Pos('unins', Lowercase(ExeName)) <> 1) then
          Result := Result + #13#10 + '   • ' + ExeName;
      end;
    end;
  except
    { no WMI – uninstall anyway, Windows reports locked files }
  end;
end;

{ The uninstaller can't delete files of running programs – ask to close them first (Setup itself closes them automatically). }
function InitializeUninstall(): Boolean;
var
  Running: String;
begin
  Result := True;
  Running := RunningFromApp();
  while Running <> '' do
  begin
    if MsgBox('MonHub läuft noch:' + Running + #13#10#13#10 +
              'Bitte schließe es zuerst (in Spielen vorher speichern!) und versuch es dann nochmal.',
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
    Running := RunningFromApp();
  end;
end;
