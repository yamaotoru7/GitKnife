; GitKnife のインストーラ（T1201）。tools/installer/build-installer.ps1 から ISCC に渡して作る。
; ユーザーごとのインストール（管理者権限なし）で %LocalAppData%\Programs\GitKnife に入れる。
; 中身は self-contained の publish（PublishDir）と同梱の git（GitDir = vendor\portable-git → {app}\git）。
; 設定（%LocalAppData%\GitKnife）はアンインストールでも消さない。

#ifndef AppVersion
  #define AppVersion "0.1.10"
#endif
#ifndef PublishDir
  #define PublishDir "..\.work\publish"
#endif
#ifndef GitDir
  #define GitDir "..\vendor\portable-git"
#endif
#ifndef OutputDir
  #define OutputDir "..\.work\installer"
#endif

[Setup]
AppId={{18E2FE2B-8D93-46A7-A9E1-F73233F0476C}
AppName=GitKnife
AppVersion={#AppVersion}
AppVerName=GitKnife {#AppVersion}
AppPublisher=toru
DefaultDirName={localappdata}\Programs\GitKnife
DefaultGroupName=GitKnife
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=GitKnife-{#AppVersion}-setup
SetupIconFile=..\src\GitKnife\Assets\GitKnife.ico
UninstallDisplayIcon={app}\GitKnife.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#GitDir}\*"; DestDir: "{app}\git"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; 版を上げたときに古い git が混ざらないように
Type: filesandordirs; Name: "{app}\git"

[Icons]
Name: "{autoprograms}\GitKnife"; Filename: "{app}\GitKnife.exe"
Name: "{autodesktop}\GitKnife"; Filename: "{app}\GitKnife.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GitKnife.exe"; Description: "{cm:LaunchProgram,GitKnife}"; Flags: nowait postinstall skipifsilent
; GitKnife の更新（Updater.cs）は /VERYSILENT /RELAUNCH=1 で起動する。入れ終わったら起動し直す
Filename: "{app}\GitKnife.exe"; Flags: nowait; Check: RelaunchAfterUpdate

[Code]
function RelaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:RELAUNCH|0}') = '1');
end;
