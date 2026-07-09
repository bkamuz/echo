; Build: .\build\installer.ps1  (requires Inno Setup 6.3+ — x64compatible in ArchitecturesAllowed / ArchitecturesInstallIn64BitMode)

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#define AppName "Echo"
#define AppPublisher "Echo"
#define AppExeName "Echo.App.exe"
#define PublishDir "..\dist\win-x64"

[Setup]
AppId={{A7B3C9E1-4F2D-4A8B-9C0E-ECHO20260409}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist\installer
OutputBaseFilename=Echo-Setup-{#AppVersion}
SetupIconFile=..\src\Echo.App\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Components]
Name: "main"; Description: "Echo"; Types: full compact custom; Flags: fixed

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Components: main; Flags: ignoreversion
Source: "{#PublishDir}\directml\*"; DestDir: "{app}\directml"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
    WizardForm.LaunchProgramCheckbox.Checked := True;
end;
