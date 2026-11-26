; SPDX-License-Identifier: GPL-2.0-or-later
; The PC-E500 emulator's installer (Inno Setup 6). Built by package.ps1, which
; passes the version: ISCC /DAppVersion=0.2.0 installer\PockeComE500.iss
;
; Installed for the current user (no administrator) by default, or for all
; users on request. Either way the program only goes here; ROMs, RAM, settings
; and state live per user in %LocalAppData%\PockeComE500, because Program
; Files cannot be written by a user. The marker file beside the executable is
; what tells the program that it was installed (see AppFolders.cs).
;
; エミュレータのインストーラー（Inno Setup 6）。package.ps1 が版を渡してビルドする。
; 既定は自分だけ（管理者の権限なし）、選べばすべてのユーザーにインストールする。
; どちらでもデータは利用者ごとの %LocalAppData%\PockeComE500 に置く。exe の横の目印のファイルで、
; プログラムはインストール版だと分かる（AppFolders.cs を参照）。

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppName "PC-E500 Emulator (PokeCom)"
#ifndef AppSource
  #define AppSource "..\dist\stage"
#endif
#define Repository "https://github.com/litchi-gh/pc-e500_emulator_pockecom"

[Setup]
AppId={{D319DFD5-6BE9-4BB5-939C-9C0F84F79001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=litchi-gh
AppPublisherURL={#Repository}
AppSupportURL={#Repository}/issues
AppUpdatesURL={#Repository}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\PockeComE500
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Current user by default; the first page offers all users (administrator).
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\src\desktop\app.ico
UninstallDisplayIcon={app}\PC-E500.exe
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=PC-E500-Emulator-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; A running emulator is asked to close (it saves its RAM and state as it does).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
japanese.DataFolder=PC-E500 Emulator のデータフォルダ
english.DataFolder=PC-E500 Emulator data folder
japanese.OpenRomFolder=ROM を置く rom フォルダを開く
english.OpenRomFolder=Open the rom folder to put a ROM in

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; package.ps1 gathers the release's files into one folder, the same one the zip
; is made from, so the two always carry the same files.
; package.ps1 が集めたフォルダ（zip と同じ中身）をそのまま入れる。
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "PockeComE500.installed"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\PC-E500 Emulator"; Filename: "{app}\PC-E500.exe"
; Opens the data folder of whoever clicks it, even when installed for all users.
Name: "{autoprograms}\{cm:DataFolder}"; Filename: "{app}\PC-E500.exe"; Parameters: "--open-folder"
Name: "{autodesktop}\PC-E500 Emulator"; Filename: "{app}\PC-E500.exe"; Tasks: desktopicon

[Run]
; Offered only while the rom folder has no ROM: a first installation, mostly.
Filename: "{app}\PC-E500.exe"; Parameters: "--open-folder rom"; Description: "{cm:OpenRomFolder}"; Flags: postinstall nowait skipifsilent runasoriginaluser; Check: NoRomYet
Filename: "{app}\PC-E500.exe"; Description: "{cm:LaunchProgram,PC-E500 Emulator}"; Flags: postinstall nowait skipifsilent runasoriginaluser

; The data folder is the user's, and stays on uninstall: only what was
; installed is removed. データフォルダは利用者のものなので、アンインストールでも残す。

[Code]
// True while %LocalAppData%\PockeComE500\rom holds no ROM image.
function NoRomYet: Boolean;
var
  Folder: String;
  Found: TFindRec;
begin
  Folder := ExpandConstant('{localappdata}\PockeComE500\rom');
  Result := True;
  if FindFirst(Folder + '\*.rom', Found) then
  begin
    Result := False;
    FindClose(Found);
  end;
end;
