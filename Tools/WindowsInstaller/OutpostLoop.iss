; 固定 AppId 和当前用户目录；只管理清单内文件，LocalLow 存档不属于安装器。
#ifndef Release
  #error Release required
#endif
[Setup]
AppId={{9E06D299-DCC5-49CF-8947-E9D9BAE9DCCA}
AppName=OutpostLoop
AppVersion={#Version}
DefaultDirName={localappdata}\Programs\OutpostLoop
UsePreviousAppDir=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=.
OutputBaseFilename={#OutputName}
Compression=lzma2/fast
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupMutex=OutpostLoopCurrentUserInstaller
UninstallDisplayIcon={app}\OutpostLoop.exe
WizardStyle=modern
[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked
[Files]
Source: "payload\windows-managed-files.txt"; Flags: dontcopy
Source: "payload\windows-payload-sha256.txt"; Flags: dontcopy
Source: "payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{userprograms}\OutpostLoop"; Filename: "{app}\OutpostLoop.exe"
Name: "{userdesktop}\OutpostLoop"; Filename: "{app}\OutpostLoop.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\OutpostLoop.exe"; Description: "Launch OutpostLoop"; Flags: postinstall nowait skipifsilent
[Code]
type
  TProcessTime = record Low, High: Cardinal; end;
function OpenProcess(Access: Cardinal; Inherit: Boolean; Id: Cardinal): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function GetProcessTimes(Handle: THandle; var Created, Exited, Kernel, User: TProcessTime): Boolean;
  external 'GetProcessTimes@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Timeout: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetFileAttributes(Path: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
var Prepared, Committed: Boolean;
    NewFiles, OldFiles: TArrayOfString;
    // 同一 AppId 在当前用户下只能有一个安装位置；升级不执行目录迁移。
    ExistingInstallDir, TransactionRoot: String;

function InitializeSetup: Boolean;
begin
  ExistingInstallDir := '';
  RegQueryStringValue(HKCU64,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{9E06D299-DCC5-49CF-8947-E9D9BAE9DCCA}_is1',
    'InstallLocation', ExistingInstallDir);
  if ExistingInstallDir <> '' then
    ExistingInstallDir := RemoveBackslashUnlessRoot(ExpandFileName(ExistingInstallDir));
  Result := True;
end;

procedure InitializeWizard;
begin
  if ExistingInstallDir <> '' then WizardForm.DirEdit.Text := ExistingInstallDir;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = wpSelectDir) and (ExistingInstallDir <> '');
end;

// 首装只允许本地磁盘内的独立空目录，避免覆盖其它应用；升级必须沿用注册目录。
procedure ValidateInstallRoot(Root: String);
var Entry: TFindRec; NonEmpty: Boolean;
begin
  if (Length(Root) <= 3) or (Copy(Root, 2, 2) <> ':\') or
     (Pos(':', Copy(Root, 3, Length(Root))) <> 0) then
    RaiseException('Choose a dedicated directory on a local drive.');
  if ExistingInstallDir <> '' then begin
    if CompareText(Root, ExistingInstallDir) <> 0 then
      RaiseException('Updates must use the existing installation directory: ' + ExistingInstallDir);
  end else begin
    NonEmpty := False;
    if FindFirst(Root + '\*', Entry) then begin
      try
        repeat
          if (Entry.Name <> '.') and (Entry.Name <> '..') then NonEmpty := True;
        until not FindNext(Entry);
      finally FindClose(Entry); end;
    end;
    if NonEmpty and not FileExists(Root + '\.update-backup\pending') then
      RaiseException('Choose an empty directory for the first installation.');
  end;
end;

// 所选安装根及托管子目录不能经过 junction/symlink，确保恢复和清理都留在安装目录。
procedure RejectLinkedParents(Path: String);
var Attributes: Cardinal; Parent: String;
begin
  while Length(Path) > 3 do begin
    Attributes := GetFileAttributes(Path);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then
      RaiseException('Installation path contains a reparse point: ' + Path);
    Parent := ExtractFileDir(Path);
    if Parent = Path then exit;
    Path := Parent;
  end;
end;

procedure RejectLinkedTree(Path: String);
var Entry: TFindRec;
begin
  RejectLinkedParents(Path);
  if FindFirst(Path + '\*', Entry) then begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then begin
          if (Entry.Attributes and $400) <> 0 then RaiseException('Reparse point in installation: ' + Entry.Name);
          if (Entry.Attributes and $10) <> 0 then RejectLinkedTree(Path + '\' + Entry.Name);
        end;
      until not FindNext(Entry);
    finally FindClose(Entry); end;
  end;
end;

// 清单路径必须为安装根目录内的托管文件；拒绝根路径、父级与控制文件。
function SafeName(Name: String): Boolean;
begin
  Result := (Name <> '') and (Pos('..', Name) = 0) and (Pos(':', Name) = 0) and
    (Pos('/', Name) = 0) and (Copy(Name, 1, 1) <> '\') and
    ((Name = 'OutpostLoop.exe') or (Name = 'windows-managed-files.txt') or (Name = 'windows-payload-sha256.txt') or (Name = 'windows-install.ini') or
     (Copy(Name, 1, 12) = 'OutpostLoop_Data\') or
     ((Pos('\', Name) = 0) and (ExtractFileExt(Name) = '.dll')));
end;

procedure ReadList(Path: String; var Files: TArrayOfString);
var I: Integer;
begin
  if not LoadStringsFromFile(Path, Files) then RaiseException('Cannot read managed file list: ' + Path);
  for I := 0 to GetArrayLength(Files)-1 do
    if not SafeName(Files[I]) then RaiseException('Invalid managed path: ' + Files[I]);
end;

procedure CopyChecked(Source, Target: String);
begin
  if not ForceDirectories(ExtractFileDir(Target)) or not FileCopy(Source, Target, False) then
    RaiseException('Cannot copy managed file: ' + Target);
end;

// 中断恢复先移除新清单文件，再恢复备份；任何失败保留日志供下次修复。
procedure Recover;
var Old, New: TArrayOfString; I: Integer; Root, Backup: String;
begin
  Root := ExpandConstant('{app}');
  if Prepared then Root := TransactionRoot;
  Backup := Root + '\.update-backup';
  RejectLinkedTree(Root);
  if not FileExists(Backup + '\pending') then exit;
  ReadList(Backup + '\new.txt', New); ReadList(Backup + '\old.txt', Old);
  for I := 0 to GetArrayLength(New)-1 do
    if FileExists(Root + '\' + New[I]) and not DeleteFile(Root + '\' + New[I]) then
      RaiseException('Close the game before recovering: ' + New[I]);
  for I := 0 to GetArrayLength(Old)-1 do
    CopyChecked(Backup + '\files\' + Old[I], Root + '\' + Old[I]);
  if not DeleteFile(Backup + '\pending') then RaiseException('Cannot finish recovery.');
  DelTree(Backup, True, True, True);
end;

// 按 PID 和创建时间识别本次游戏实例；不终止任何进程，最长等待 60 秒。
function WaitForGame: String;
var Pid: Integer; Handle: THandle; Created, Exited, Kernel, User: TProcessTime;
begin
  Result := ''; Pid := StrToIntDef(ExpandConstant('{param:WAITPID|0}'), 0);
  if Pid = 0 then exit;
  Handle := OpenProcess($00101000, False, Pid);
  if Handle = 0 then exit;
  try
    if not GetProcessTimes(Handle, Created, Exited, Kernel, User) then
      Result := 'Cannot identify the game process. Close the game and retry.'
    else if (Int64(Created.Low) = StrToInt64Def(ExpandConstant('{param:WAITLOW|0}'), 0)) and
            (Int64(Created.High) = StrToInt64Def(ExpandConstant('{param:WAITHIGH|0}'), 0)) then
      if WaitForSingleObject(Handle, 60000) <> 0 then Result := 'Close the game and retry installation.';
  finally CloseHandle(Handle); end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var I: Integer; Root, Backup: String; Existing: TArrayOfString; Free, Total, Required, Size: Int64;
begin
  Result := WaitForGame;
  if Result <> '' then exit;
  try
    Root := RemoveBackslashUnlessRoot(ExpandFileName(ExpandConstant('{app}')));
    if Prepared then begin
      if CompareText(Root, TransactionRoot) <> 0 then
        RaiseException('The installation directory cannot change after preparing. Cancel and restart setup.');
      exit;
    end;
    ValidateInstallRoot(Root);
    Recover;
    Root := ExpandConstant('{app}'); Backup := Root + '\.update-backup';
    if StrToIntDef(GetIniString('Release', 'Number', '0', Root + '\windows-install.ini'), 0) > {#Release} then
      RaiseException('A newer release is already installed. Downgrade is not allowed.');
    ExtractTemporaryFile('windows-managed-files.txt');
    ReadList(ExpandConstant('{tmp}\windows-managed-files.txt'), NewFiles);
    SetArrayLength(OldFiles, 0);
    if FileExists(Root + '\windows-managed-files.txt') then
      ReadList(Root + '\windows-managed-files.txt', OldFiles)
    else if FileExists(Root + '\OutpostLoop.exe') then
      RaiseException('Unknown existing files. Move the portable game out of the install directory first.');
    Required := StrToInt64('{#PayloadBytes}') + 67108864;
    for I := 0 to GetArrayLength(OldFiles)-1 do
      if FileSize64(Root + '\' + OldFiles[I], Size) then Required := Required + Size;
    if not GetSpaceOnDisk64(ExtractFileDrive(Root) + '\', Free, Total) or (Free < Required) then
      RaiseException('Insufficient disk space for installation and recovery backup.');
    // 备份尚未完成时没有 pending，旧程序不会被改动；先清理上次未完成的备份。
    if DirExists(Backup) and not DelTree(Backup, True, True, True) then RaiseException('Cannot clear incomplete backup.');
    ForceDirectories(Backup); SetArrayLength(Existing, 0);
    for I := 0 to GetArrayLength(OldFiles)-1 do
      if FileExists(Root + '\' + OldFiles[I]) then begin
        CopyChecked(Root + '\' + OldFiles[I], Backup + '\files\' + OldFiles[I]);
        SetArrayLength(Existing, GetArrayLength(Existing)+1);
        Existing[GetArrayLength(Existing)-1] := OldFiles[I];
      end;
    if not SaveStringsToUTF8File(Backup + '\old.txt', Existing, False) or
       not SaveStringsToUTF8File(Backup + '\new.txt', NewFiles, False) or
       not SaveStringToFile(Backup + '\pending', '1', False) then RaiseException('Cannot write recovery journal.');
    TransactionRoot := Root;
    Prepared := True;
  except Result := GetExceptionMessage; end;
end;

// 禁止安装向导的“忽略文件写入错误”造成新旧二进制混用；全部验证后才提交。
procedure VerifyPayload;
var Entries: TArrayOfString; I, Separator: Integer; Name, Expected, Root: String;
begin
  ExtractTemporaryFile('windows-payload-sha256.txt');
  if not LoadStringsFromFile(ExpandConstant('{tmp}\windows-payload-sha256.txt'), Entries) then
    RaiseException('Cannot read payload hashes.');
  Root := ExpandConstant('{app}');
  for I := 0 to GetArrayLength(Entries)-1 do begin
    Separator := Pos('|', Entries[I]);
    if Separator <> 65 then RaiseException('Invalid payload hash.');
    Expected := Copy(Entries[I], 1, 64); Name := Copy(Entries[I], 66, Length(Entries[I]));
    if not SafeName(Name) then RaiseException('Invalid payload hash path.');
    if not FileExists(Root + '\' + Name) then RaiseException('Incomplete installation: ' + Name);
    if CompareText(GetSHA256OfFile(Root + '\' + Name), Expected) <> 0 then
      RaiseException('Installed file validation failed: ' + Name);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var I, J: Integer; Present: Boolean; Root: String;
begin
  if CurStep <> ssPostInstall then exit;
  Root := ExpandConstant('{app}');
  VerifyPayload;
  for I := 0 to GetArrayLength(OldFiles)-1 do begin
    Present := False;
    for J := 0 to GetArrayLength(NewFiles)-1 do
      if CompareText(OldFiles[I], NewFiles[J]) = 0 then Present := True;
    if not Present and FileExists(Root + '\' + OldFiles[I]) and not DeleteFile(Root + '\' + OldFiles[I]) then
      RaiseException('Cannot remove obsolete managed file: ' + OldFiles[I]);
  end;
  if not DeleteFile(Root + '\.update-backup\pending') then RaiseException('Cannot commit installation.');
  Committed := True;
  DelTree(Root + '\.update-backup', True, True, True);
end;

procedure DeinitializeSetup;
begin
  if Prepared and not Committed then
    try Recover; except MsgBox('Recovery incomplete. Run this installer again after closing the game.', mbError, MB_OK); end;
end;
