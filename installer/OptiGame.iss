; Installeur d'OptiGame (Inno Setup 6). Fabriqué par scripts\build-installer.ps1, qui publie d'abord l'appli en version
; autonome (.NET inclus : rien à installer sur la machine) dans artifacts\publish et y ajoute PresentMon (tools\).
; Compilation à la main : ISCC.exe /DAppVersion=1.0.0 /DPublishDir=<dossier publié> installer\OptiGame.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif

[Setup]
; Identifiant DÉFINITIF : c'est lui qui fait d'une nouvelle version la mise à jour de la précédente. Ne jamais le changer.
AppId={{BEC983EC-07E6-4D4C-A824-72F7135018FA}
AppName=OptiGame
AppVersion={#AppVersion}
AppVerName=OptiGame {#AppVersion}
AppPublisher=OptiGame
AppCopyright=© 2026 OptiGame
VersionInfoVersion={#AppVersion}
VersionInfoDescription=Installation d'OptiGame
DefaultDirName={autopf}\OptiGame
DisableProgramGroupPage=yes
; OptiGame tourne en administrateur : il est installé là où seul un administrateur peut écrire (Program Files), jamais
; dans le profil de l'utilisateur, où un autre programme pourrait remplacer l'exe lancé avec ces droits. Pas de choix du
; dossier : sur un autre disque, tout utilisateur peut écrire par défaut (et OptiGame ne s'y mettrait pas à jour lui-même).
DisableDirPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 ou plus récent, et Windows 11.
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=OptiGame-Setup-{#AppVersion}
SetupIconFile=..\src\OptiGame.App\Assets\OptiGame.ico
UninstallDisplayIcon={app}\OptiGame.exe
UninstallDisplayName=OptiGame
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Fichiers encore utilisés après la fermeture propre (voir [Code]) : proposer de fermer les programmes concernés.
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Messages]
french.ConfirmUninstall=Désinstaller %1 ?%n%nLes corrections appliquées par OptiGame (diagnostic, plafonds de FPS du pilote NVIDIA…) restent en place : pour les annuler, faites-le depuis OptiGame avant de continuer.%n%nVos profils, mesures et réglages sont gardés dans %%LocalAppData%%\OptiGame.

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Mise à jour : seul le PresentMon fourni par cette version reste (OptiGame prend le plus récent du dossier).
Type: files; Name: "{app}\tools\PresentMon-*.exe"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\OptiGame"; Filename: "{app}\OptiGame.exe"
Name: "{autodesktop}\OptiGame"; Filename: "{app}\OptiGame.exe"; Tasks: desktopicon

[Run]
; Lancé au nom de l'utilisateur qui installe (et non du compte administrateur qui a validé l'installation) : ses données
; vont dans SON profil. OptiGame demande lui-même ses droits (invite UAC).
Filename: "{app}\OptiGame.exe"; Description: "{cm:LaunchProgram,OptiGame}"; Flags: nowait postinstall skipifsilent runasoriginaluser shellexec

[UninstallDelete]
; Installeurs téléchargés par la mise à jour automatique d'OptiGame (dossier créé par l'appli, pas par l'installeur).
Type: filesandordirs; Name: "{app}\updates"

[UninstallRun]
; Tâche planifiée du démarrage automatique (Paramètres d'OptiGame) : sans l'exe, elle n'a plus de raison d'être.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""OptiGame"" /F"; Flags: runhidden; RunOnceId: "DelAutoStartTask"

[Code]
const
  DataDir = '{localappdata}\OptiGame';

function OptiGameIsRunning(): Boolean;
var
  Locator, Service, Found: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Found := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''OptiGame.exe''');
    Result := Found.Count > 0;
  except
    Result := False;
  end;
end;

{ Fermeture propre, comme scripts\dev-run.ps1 : OptiGame (en administrateur) surveille quit.request dans son dossier de
  données et, pendant une partie, restaure d'abord les réglages modifiés. Vrai si OptiGame est fermé (ou ne tournait pas). }
function AskOptiGameToQuit(): Boolean;
var
  Waited: Integer;
begin
  Result := not OptiGameIsRunning();
  if Result then
    Exit;
  ForceDirectories(ExpandConstant(DataDir));
  SaveStringToFile(ExpandConstant(DataDir + '\quit.request'), GetDateTimeString('yyyy/mm/dd hh:nn:ss', '-', ':'), False);
  Waited := 0;
  while OptiGameIsRunning() and (Waited < 20000) do
  begin
    Sleep(250);
    Waited := Waited + 250;
  end;
  Result := not OptiGameIsRunning();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not AskOptiGameToQuit() then
    Result := 'OptiGame ne s''est pas fermé. Quittez-le (clic droit sur son icône près de l''horloge, puis « Quitter »), puis relancez l''installation.';
end;

{ Mise à jour lancée par OptiGame lui-même (/RELAUNCH=minimized ou /RELAUNCH=window, sans aucune fenêtre d'installation) :
  il s'est fermé pour être remplacé, il est relancé à la fin, que l'installation ait réussi ou non (sinon il resterait arrêté
  jusqu'à la prochaine ouverture de session). Lancé par l'installeur, donc avec ses droits administrateur : pas d'invite UAC. }
procedure DeinitializeSetup();
var
  Mode, Exe: String;
  ResultCode: Integer;
begin
  Mode := ExpandConstant('{param:RELAUNCH|}');
  if (Mode = '') or OptiGameIsRunning() then
    Exit;
  try
    Exe := ExpandConstant('{app}\OptiGame.exe');
  except
    Exit; { installation arrêtée avant que le dossier soit connu }
  end;
  if not FileExists(Exe) then
    Exit;
  if Mode = 'minimized' then
    Exec(Exe, '--minimized', '', SW_SHOWNORMAL, ewNoWait, ResultCode)
  else
    Exec(Exe, '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not OptiGameIsRunning() then
    Exit;
  if SuppressibleMsgBox('OptiGame est ouvert : il va être fermé (une partie en cours est d''abord restaurée). Continuer ?',
    mbConfirmation, MB_OKCANCEL, IDOK) <> IDOK then
  begin
    Result := False;
    Exit;
  end;
  if not AskOptiGameToQuit() then
  begin
    SuppressibleMsgBox('OptiGame ne s''est pas fermé. Quittez-le (clic droit sur son icône près de l''horloge, puis « Quitter »), puis relancez la désinstallation.',
      mbError, MB_OK, IDOK);
    Result := False;
  end;
end;
