; Installer for MonHub (launcher with its randomizer + melonDS + DeSmuME + mGBA + Azahar). Build with build.ps1.
; Everything goes below ONE folder; ROMs, saves, randomized ROMs and fangames survive an uninstall.

#define AppName "MonHub"
#define AppVersion "2.9.0"
#define Root "..\.."
#define BuildDir Root + "\Hub-Build"

[Setup]
AppId={{9B6C2F4E-3E1A-4B8D-A7C2-5D0E8F1A2B77}
AppName={#AppName}
AppVersion={#AppVersion}
; the search in Settings > Apps matches name OR publisher
AppPublisher=MonHub
; earlier installs (named MonHub) keep their folder – Setup reuses the previous one
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
SetupIconFile=..\monhub.ico
UninstallDisplayIcon={app}\System\App\MonHub.exe
UninstallDisplayName={#AppName}
; English first and preselected (MonHub's default); the choice also becomes MonHub's language on a new install
ShowLanguageDialog=yes
LanguageDetectionMethod=none

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Messages]
english.WelcomeLabel1=Welcome to MonHub!
english.WelcomeLabel2=Your game hub: randomizer, emulators for Game Boy, GBA, DS and 3DS and your fangames – all in one window.%n%nThe installation only takes a moment. You bring your own ROMs – MonHub helps you import them on the first start.
english.FinishedHeadingLabel=Done – have fun!
english.FinishedLabel=MonHub is installed.%n%nOn the first start Porygon shows you where everything is. You'll find MonHub in the Start menu, on the desktop or through the Windows search.
english.FinishedLabelNoIcons=MonHub is installed.%n%nOn the first start Porygon shows you where everything is. You'll find MonHub through the Windows search ("MonHub").
english.ClickFinish=Click "Finish".
english.WizardSelectTasks=Desktop icon
english.SelectTasksDesc=Almost done!
english.SelectTasksLabel2=Should MonHub get an icon on the desktop too? You'll find it in the Start menu and the Windows search anyway.
; ROMs, saves and fangames are kept on purpose – the final uninstall message says so, otherwise the folder looks like a leftover
english.UninstalledAll=%1 was removed.%n%nYour ROMs, saves and randomized games are still there – in the MonHub folder (the one you chose when installing). If you don't need them anymore, you can delete the folder yourself.
german.WelcomeLabel1=Willkommen bei MonHub!
german.WelcomeLabel2=Dein Spielzentrum: Randomizer, Emulatoren für Game Boy, GBA, DS und 3DS und deine Fangames – alles in einem Fenster.%n%nDie Installation dauert nur einen Moment. Deine ROMs bringst du selbst mit – MonHub hilft dir beim ersten Start, sie zu importieren.
german.FinishedHeadingLabel=Fertig – viel Spaß!
german.FinishedLabel=MonHub ist installiert.%n%nBeim ersten Start zeigt dir Porygon, wo alles ist. Du findest MonHub im Startmenü, auf dem Desktop oder über die Windows-Suche.
german.FinishedLabelNoIcons=MonHub ist installiert.%n%nBeim ersten Start zeigt dir Porygon, wo alles ist. Du findest MonHub über die Windows-Suche („MonHub“).
german.ClickFinish=Klicke auf „Fertigstellen“.
german.WizardSelectTasks=Desktop-Symbol
german.SelectTasksDesc=Fast fertig!
german.SelectTasksLabel2=Soll MonHub auch ein Symbol auf dem Desktop bekommen? Im Startmenü und in der Windows-Suche findest du es sowieso.
german.UninstalledAll=%1 wurde entfernt.%n%nDeine ROMs, Spielstände und randomisierten Spiele sind noch da – im MonHub-Ordner (den du bei der Installation gewählt hast). Wenn du sie nicht mehr brauchst, kannst du den Ordner selbst löschen.

[CustomMessages]
english.ShortcutComment=Randomizer, emulators (Game Boy, GBA, DS, 3DS) and fangames
german.ShortcutComment=Randomizer, Emulatoren (Game Boy, GBA, DS, 3DS) und Fangames
english.OpenGuide=Open the guide (README)
german.OpenGuide=Anleitung (LIESMICH) öffnen
english.GuideFile=README.txt
german.GuideFile=LIESMICH.txt
english.StillRunning=MonHub is still running:
german.StillRunning=MonHub läuft noch:
english.CloseFirst=Please close it first (save in your games before!) and try again.
german.CloseFirst=Bitte schließe es zuerst (in Spielen vorher speichern!) und versuch es dann nochmal.

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
Source: "README.txt"; DestDir: "{app}"; Flags: ignoreversion

[INI]
; a new stamp on every install (also the same version again): MonHub's Porygon then offers the tour
Filename: "{app}\System\App\install.ini"; Section: "Setup"; Key: "Version"; String: "{#AppVersion}"
Filename: "{app}\System\App\install.ini"; Section: "Setup"; Key: "Stamp"; String: "{code:InstallStamp}"
; the language chosen in Setup: MonHub starts in it as long as the player hasn't picked one in MonHub
Filename: "{app}\System\App\install.ini"; Section: "Setup"; Key: "Language"; String: "{language}"

[Icons]
; one clean Start menu entry – found by the Windows search like any installed app
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\System\App\MonHub.exe"; Comment: "{cm:ShortcutComment}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\System\App\MonHub.exe"; Tasks: desktopicon

[InstallDelete]
; up to 2.4: a test preset, and the default preset under its old name (now Randomizer_Nuzlock_with_Items)
Type: files; Name: "{app}\System\App\Settings\test1.rnqs"
Type: files; Name: "{app}\System\App\Settings\Merlinstehtaufkleinkinder.rnqs"
; ---- an update from an older version: its files under their OLD names go (the player's folders stay) ----
; up to 2.4 the app had its old name: its program files and shortcuts
Type: files; Name: "{app}\System\App\PokeHub.*"
Type: files; Name: "{autoprograms}\PokéHub.lnk"
Type: files; Name: "{autodesktop}\PokéHub.lnk"
; up to 2.6 the randomizer was shipped under its own file name (now randomizer.jar)
Type: files; Name: "{app}\System\App\PokeRandoZX.jar"
; up to 2.8 MonHub was built on another UI technology: its program libraries (about 100 MB) are not used any more.
; All libraries go – the ones of this version are copied right after – and the old ones' language folders.
Type: files; Name: "{app}\System\App\*.dll"
Type: filesandordirs; Name: "{app}\System\App\cs"
Type: filesandordirs; Name: "{app}\System\App\de"
Type: filesandordirs; Name: "{app}\System\App\es"
Type: filesandordirs; Name: "{app}\System\App\fr"
Type: filesandordirs; Name: "{app}\System\App\it"
Type: filesandordirs; Name: "{app}\System\App\ja"
Type: filesandordirs; Name: "{app}\System\App\ko"
Type: filesandordirs; Name: "{app}\System\App\pl"
Type: filesandordirs; Name: "{app}\System\App\pt-BR"
Type: filesandordirs; Name: "{app}\System\App\ru"
Type: filesandordirs; Name: "{app}\System\App\tr"
Type: filesandordirs; Name: "{app}\System\App\zh-Hans"
Type: filesandordirs; Name: "{app}\System\App\zh-Hant"
; up to 2.4 the stand-alone randomizer app came along
Type: files; Name: "{app}\System\App\RandoApp.*"
; 1.0 had a second Start menu entry that showed up as an extra "app" in the search
Type: files; Name: "{autoprograms}\PokéHub – Liesmich.lnk"

[Run]
Filename: "{app}\System\App\MonHub.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; the long guide stays in the folder; here only on request
Filename: "{app}\{cm:GuideFile}"; Description: "{cm:OpenGuide}"; Flags: shellexec nowait postinstall skipifsilent unchecked

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
    if MsgBox(CustomMessage('StillRunning') + Running + #13#10#13#10 + CustomMessage('CloseFirst'),
              mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
    Running := RunningFromApp();
  end;
end;
