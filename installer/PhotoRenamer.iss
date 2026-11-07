; 写真・動画リネーマー インストーラー (Inno Setup 6)
; ビルド: build.ps1 を実行するか、ISCC.exe PhotoRenamer.iss

#define AppName "写真・動画リネーマー"
#define AppVersion "2.0.0"
#define AppExe "PhotoRenamer.exe"
#define SrcDir "..\PhotoRenamer\bin\Release\net48"

[Setup]
AppId={{6B7E2F3A-4C1D-4E8B-9A2F-5D3C7B1E9F40}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=PhotoRenamer
DefaultDirName={autopf}\PhotoRenamer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; 管理者権限なしでインストールできる（「すべてのユーザー」も選択可）
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist-installer
OutputBaseFilename=PhotoRenamer_Setup_{#AppVersion}
SetupIconFile=..\PhotoRenamer\camera.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
Name: "desktopicon"; Description: "デスクトップにショートカットを作る"; GroupDescription: "追加の設定:"
Name: "contextmenu"; Description: "フォルダの右クリックメニューに「写真・動画リネーマーで開く」を追加"; GroupDescription: "追加の設定:"

[Files]
Source: "{#SrcDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SrcDir}\{#AppExe}.config"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoRenamer"; ValueType: string; ValueName: ""; ValueData: "写真・動画リネーマーで開く"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoRenamer"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#AppExe}"""; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\PhotoRenamer\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu

[Run]
Filename: "{app}\{#AppExe}"; Description: "{#AppName} を起動する"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\PhotoRenamer"

[Code]
// .NET Framework 4.8 (Windows 10 1903 以降は標準搭載) があるか確認
function InitializeSetup(): Boolean;
var
  release: Cardinal;
  err: Integer;
begin
  Result := True;
  if not RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) or (release < 528040) then
  begin
    if MsgBox('このアプリには .NET Framework 4.8 が必要です。' + #13#10 +
              'ダウンロードページを開きますか？', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOWNORMAL, ewNoWait, err);
    Result := False;
  end;
end;
