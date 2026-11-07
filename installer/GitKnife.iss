; GitKnife のインストーラ（T1201）。tools/installer/build-installer.ps1 から ISCC に渡して作る。
; ユーザーごとのインストール（管理者権限なし）で %LocalAppData%\Programs\GitKnife に入れる。
; 中身は self-contained の publish（PublishDir）と同梱の git（GitDir = vendor\portable-git → {app}\git）。
; 設定（%LocalAppData%\GitKnife）はアンインストールでも消さない。

#ifndef AppVersion
  #define AppVersion "0.1.16"
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

[UninstallDelete]
Type: files; Name: "{app}\install-source.txt"

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

// ダウンロード元（ブラウザがこのファイルに書く Zone.Identifier の HostUrl と ReferrerUrl）を {app}\install-source.txt に写す。
// GitKnife はここから更新とバグ報告の送り先を決める（Distribution.cs）。インストーラはどの配布先でも同じもの。
// Zone.Identifier が無いとき（GitKnife の更新が落としたもの、コピーしたもの）は前に書いたものを残す。
function SaveInstallSource(const SetupPath, OutPath: String): Boolean;
var
  Lines: TArrayOfString;
  I: Integer;
  Text: String;
begin
  Result := False;
  if not LoadStringsFromFile(SetupPath + ':Zone.Identifier', Lines) then Exit;
  Text := '';
  for I := 0 to GetArrayLength(Lines) - 1 do
    if (Pos('HostUrl=', Lines[I]) = 1) or (Pos('ReferrerUrl=', Lines[I]) = 1) then
      Text := Text + Lines[I] + #13#10;
  if Text = '' then Exit;
  Result := SaveStringToFile(OutPath, Text, False);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if SaveInstallSource(ExpandConstant('{srcexe}'), ExpandConstant('{app}\install-source.txt')) then
      Log('Saved the download source to install-source.txt')
    else
      Log('No download source (Zone.Identifier); keeping install-source.txt as it is');
end;
