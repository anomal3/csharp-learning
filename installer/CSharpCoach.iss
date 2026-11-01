; Установщик CSharpCoach. Собирается скриптом build.ps1 (он же передаёт AppVersion и SourceDir).
; Ставится для текущего пользователя (без прав администратора) — поэтому автообновление идёт без запроса UAC.
; Права администратора нужны один раз и только если на ПК нет .NET 10 Desktop Runtime: он ставится из комплекта.
;
; ЧИСТАЯ БАЗА: в установщик попадает только content\course.db (уроки и вопросы, без чьего-либо прогресса).
; Прогресс ученика (progress.db) создаётся самой программой при первом запуске в %LOCALAPPDATA%\CsharpLearning
; и установщиком никогда не перезаписывается и не поставляется.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "app"
#endif

[Setup]
AppId={{6F1C3B52-8D0A-4E6B-9C47-2B5A71D3E9A4}
AppName=CSharpCoach
AppVersion={#AppVersion}
AppVerName=CSharpCoach {#AppVersion}
AppPublisher=BlueBox Production
DefaultDirName={autopf}\CSharpCoach
DefaultGroupName=CSharpCoach
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=CSharpCoach-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CSharpCoach.exe
; программа сама запускает установщик при обновлении: закрыть её и открыть заново
CloseApplications=yes
SetupLogging=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
; программа, библиотеки, reference-сборки для запуска примеров (refs\) и чистая база курса (content\course.db)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb,progress.db,progress.db-*,settings.json,course.db.prev,*.csgraph"
; среда выполнения .NET 10 Desktop Runtime — распаковывается только если её нет на ПК
Source: "redist\windowsdesktop-runtime-win-x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsRuntime

[Icons]
Name: "{group}\CSharpCoach"; Filename: "{app}\CSharpCoach.exe"
Name: "{group}\Удалить CSharpCoach"; Filename: "{uninstallexe}"
Name: "{autodesktop}\CSharpCoach"; Filename: "{app}\CSharpCoach.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CSharpCoach.exe"; Description: "Запустить CSharpCoach"; Flags: nowait postinstall skipifsilent; Check: RuntimeReady
; автообновление запускает установщик с /SILENT: программа к этому моменту уже закрыта — открываем её снова
Filename: "{app}\CSharpCoach.exe"; Flags: nowait runasoriginaluser; Check: RelaunchAfterAutoUpdate and RuntimeReady

[Code]
const
  RuntimeKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';

var
  RuntimeFailed: Boolean;

function RegHas10(RootKey: Integer): Boolean;
var
  Names: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetValueNames(RootKey, RuntimeKey, Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 3) = '10.' then
      begin
        Result := True;
        Exit;
      end;
end;

// Есть ли на ПК любая 10.x версия .NET Desktop Runtime (x64).
// Смотрим папку dotnet (там же лежит dotnet.exe, который нужен запуску примеров) и оба представления реестра:
// установщик среды записывает ключ в зависимости от разрядности по-разному.
function HasRuntime10: Boolean;
var
  Found: TFindRec;
begin
  Result := False;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\10.*'), Found) then
  begin
    Result := True;
    FindClose(Found);
    Exit;
  end;
  Result := RegHas10(HKLM64) or RegHas10(HKLM32);
end;

// Программу запускаем только если среда выполнения на месте
function RuntimeReady: Boolean;
begin
  Result := not RuntimeFailed;
end;

function HasParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

// «/SILENT» (но не «/VERYSILENT») — так программа запускает установщик при автообновлении
function RelaunchAfterAutoUpdate: Boolean;
begin
  Result := HasParam('/SILENT');
end;

function NeedsRuntime: Boolean;
begin
  Result := not HasRuntime10;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
  Installer, Params: String;
  Started: Boolean;
begin
  if (CurStep = ssPostInstall) and NeedsRuntime then
  begin
    Installer := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
    WizardForm.StatusLabel.Caption := 'Устанавливаю .NET 10 Desktop Runtime. Подтвердите запрос администратора и дождитесь окончания…';
    WizardForm.FilenameLabel.Caption := '';
    WizardForm.ProgressGauge.Style := npbstMarquee;

    // /passive — окно установщика Microsoft с полосой прогресса (без вопросов); в тихом режиме — /quiet
    if WizardSilent then Params := '/install /quiet /norestart' else Params := '/install /passive /norestart';

    // runas: права администратора нужны только для среды выполнения; сама программа ставится без них.
    // ewWaitUntilTerminated — установщик CSharpCoach стоит и ждёт, пока среда не установится целиком.
    Started := ShellExec('runas', Installer, Params, '', SW_SHOW, ewWaitUntilTerminated, Code);

    WizardForm.ProgressGauge.Style := npbstNormal;
    Log(Format('Runtime installer: started=%d code=%d', [Ord(Started), Code]));

    // итог решает не код возврата, а то, что среда действительно появилась на диске
    if not HasRuntime10 then
    begin
      RuntimeFailed := True;
      if not Started then
        SuppressibleMsgBox('Установка .NET 10 Desktop Runtime отменена: не получено разрешение администратора. ' +
          'CSharpCoach установлен, но не запустится, пока не будет среды выполнения. ' +
          'Запустите установщик ещё раз или поставьте её вручную: https://dotnet.microsoft.com/download/dotnet/10.0',
          mbError, MB_OK, IDOK)
      else
        SuppressibleMsgBox('Не удалось установить .NET 10 Desktop Runtime (код ' + IntToStr(Code) + '). ' +
          'Установите её вручную: https://dotnet.microsoft.com/download/dotnet/10.0 — без неё программа не запустится.',
          mbError, MB_OK, IDOK);
    end;
  end;
end;

// При удалении спрашиваем, что делать с прогрессом ученика (по умолчанию он сохраняется)
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Data: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    Data := ExpandConstant('{localappdata}\CsharpLearning');
    if DirExists(Data) and
       (not UninstallSilent) and
       (MsgBox('Удалить также ваш прогресс, ответы и сохранённые графы?' + #13#10 + Data + #13#10#13#10 +
               'Выберите «Нет», если планируете установить программу снова — прогресс сохранится.',
               mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      DelTree(Data, True, True, True);
  end;
end;
