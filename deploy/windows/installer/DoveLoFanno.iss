; Installatore di DoveLoFanno per Windows (Inno Setup 6). Lo compila tools/ci/build-installer.ps1:
;   ISCC.exe /DAppVersion=0.1.0 /DPackageDir=<cartella del dotnet publish win-x64> /DOutputDir=<uscita> DoveLoFanno.iss
; Fa tutto da solo: runtime ASP.NET Core 10 da Microsoft se manca, ffmpeg con winget se manca, programma in
; Programmi\DoveLoFanno, servizio Windows con avvio automatico, porta 8765 aperta solo sulle reti private.
; Aggiornare = lanciare l'installatore della versione nuova (dati e registrazioni restano). Disinstallare = Impostazioni
; di Windows › App: chiede se togliere anche il database; le registrazioni restano sempre.
; Senza finestre (collaudi): /VERYSILENT /SUPPRESSMSGBOXES /RECORDINGS="D:\Registrazioni"

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PackageDir
  #error "manca /DPackageDir=<cartella del programma pubblicato>"
#endif
#ifndef OutputDir
  #define OutputDir "out"
#endif

[Setup]
AppId={{6B0E2C41-7F3A-4D8E-9C55-D0F1E2A3B4C5}
AppName=DoveLoFanno
AppVersion={#AppVersion}
AppVerName=DoveLoFanno {#AppVersion}
AppPublisher=DoveLoFanno
AppPublisherURL=https://dovelofanno.it
AppSupportURL=https://dovelofanno.it
AppUpdatesURL=https://dovelofanno.it
AppCopyright=Software libero, GPL-3.0 o successive
VersionInfoVersion={#Copy(AppVersion, 1, Pos("-", AppVersion + "-") - 1)}
VersionInfoProductName=DoveLoFanno
VersionInfoDescription=Installatore di DoveLoFanno
DefaultDirName={autopf}\DoveLoFanno
DisableDirPage=auto
DisableProgramGroupPage=yes
DisableReadyPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=DoveLoFanno.ico
UninstallDisplayIcon={app}\DoveLoFanno.ico
UninstallDisplayName=DoveLoFanno
OutputDir={#OutputDir}
OutputBaseFilename=DoveLoFanno-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
CloseApplications=no
SetupLogging=yes

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}\server"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "..\Install-DoveLoFanno.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "..\Uninstall-DoveLoFanno.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "..\Prepare-DoveLoFanno.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "DoveLoFanno.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
; a ogni aggiornamento il programma si sostituisce intero (niente file rimasti dalla versione prima)
Type: filesandordirs; Name: "{app}\server"

[UninstallDelete]
; la configurazione (appsettings.Production.json) la scrive Install-DoveLoFanno.ps1, non l'installatore
Type: filesandordirs; Name: "{app}\server"

[Icons]
Name: "{autoprograms}\DoveLoFanno"; Filename: "http://localhost:8765/"; IconFilename: "{app}\DoveLoFanno.ico"; Comment: "Apre DoveLoFanno nel browser"

[Run]
Filename: "http://localhost:8765/"; Description: "Apri DoveLoFanno nel browser"; Flags: postinstall shellexec nowait skipifsilent; Check: InstallOk

[Code]
var
  RecPage: TInputDirWizardPage;
  Failed: Boolean;

function LogFile(): String;
begin
  Result := ExpandConstant('{commonappdata}\DoveLoFanno\logs\installazione.log');
end;

function PowerShell(const Script, Args: String; var Code: Integer): Boolean;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" ' + Args + ' -Log "' + LogFile() + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

function DefaultRecordings(): String;
begin
  Result := ExpandConstant('{param:RECORDINGS|}');
  if Result = '' then
    Result := GetPreviousData('RecordingsDir', AddBackslash(GetEnv('PUBLIC')) + 'Videos\DoveLoFanno');
end;

procedure InitializeWizard();
begin
  RecPage := CreateInputDirPage(wpSelectDir, 'Registrazioni',
    'Dove salvo le registrazioni?',
    'DoveLoFanno salva qui i programmi che registri. Scegli un disco con spazio: un''ora di TV occupa circa 2,5 GB.' + #13#10 +
    'Puoi usare anche una cartella che leggono Jellyfin o Plex.',
    False, 'DoveLoFanno');
  RecPage.Add('');
  RecPage.Values[0] := DefaultRecordings();
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'RecordingsDir', RecPage.Values[0]);
end;

function InstallOk(): Boolean;
begin
  Result := not Failed;
end;

// prima di sostituire i file: fermare il servizio (aggiornamento)
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
  Script: String;
begin
  Result := '';
  Script := ExpandConstant('{tmp}\Prepare-DoveLoFanno.ps1');
  ExtractTemporaryFile('Prepare-DoveLoFanno.ps1');
  if not PowerShell(Script, '-Step stop', Code) then
    Result := 'Non riesco a fermare il servizio DoveLoFanno della versione installata. Dettagli in ' + LogFile();
end;

function RunStep(const Status, Script, Args: String): Boolean;
var
  Code: Integer;
begin
  WizardForm.StatusLabel.Caption := Status;
  WizardForm.FilenameLabel.Caption := '';
  WizardForm.ProgressGauge.Style := npbstMarquee;
  try
    Result := PowerShell(Script, Args, Code);
  finally
    WizardForm.ProgressGauge.Style := npbstNormal;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Setup, Why: String;
begin
  if CurStep <> ssPostInstall then exit;
  Setup := ExpandConstant('{app}\setup\');
  Why := '';
  if not RunStep('Controllo il runtime .NET di Microsoft (se manca lo scarico da microsoft.com)…', Setup + 'Prepare-DoveLoFanno.ps1', '-Step runtime') then
    Why := 'Il runtime .NET di Microsoft non si è installato.'
  else if not RunStep('Controllo ffmpeg (se manca lo installo con winget)…', Setup + 'Prepare-DoveLoFanno.ps1', '-Step ffmpeg') then
    Why := 'ffmpeg non si è installato.'
  else if not RunStep('Avvio il servizio DoveLoFanno…', Setup + 'Install-DoveLoFanno.ps1',
      '-Package "' + ExpandConstant('{app}\server') + '" -InstallDir "' + ExpandConstant('{app}\server') + '" -NoCopy -RecordingsDir "' + RecPage.Values[0] + '"') then
    Why := 'Il servizio DoveLoFanno non è partito.';
  if Why <> '' then
  begin
    Failed := True;
    SuppressibleMsgBox(Why + #13#10#13#10 + 'Il registro con i dettagli è in:' + #13#10 + LogFile() + #13#10#13#10 +
      'Puoi rilanciare l''installazione quando hai risolto, oppure scrivere a beta@dovelofanno.it allegando il registro.',
      mbError, MB_OK, IDOK);
  end;
end;

// installazione non riuscita: l'installatore esce con un codice d'errore (utile nei collaudi senza finestre)
function GetCustomSetupExitCode(): Integer;
begin
  if Failed then Result := 1 else Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
  Args: String;
begin
  if CurUninstallStep <> usUninstall then exit;
  Args := '-KeepFiles';
  if not UninstallSilent() then
    if MsgBox('Togliere anche database, impostazioni e copie di sicurezza di DoveLoFanno?' + #13#10#13#10 +
        'Le registrazioni restano comunque nella loro cartella. Rispondi No se pensi di reinstallarlo.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Args := Args + ' -RemoveData';
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\setup\Uninstall-DoveLoFanno.ps1') + '" ' + Args +
    ' -InstallDir "' + ExpandConstant('{app}\server') + '"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
