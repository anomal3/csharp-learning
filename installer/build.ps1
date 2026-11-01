# Сборка релиза CSharpCoach: чистая база -> publish -> установщик (Inno Setup) -> запись "app" в publish\manifest.json.
# Запуск:  powershell -ExecutionPolicy Bypass -File installer\build.ps1
# Результат: installer\Output\CSharpCoach-Setup-<версия>.exe и publish\manifest.json с блоком "app".
# На хостинг (https://blueboxproduction.ru/csharp/) кладутся: manifest.json, course-vN.db.gz и CSharpCoach-Setup-<версия>.exe.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root 'src\CsharpLearning.App\CsharpLearning.App.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in csproj' }
Write-Host "== CSharpCoach $version =="

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }
if (-not (Test-Path "$PSScriptRoot\redist\windowsdesktop-runtime-win-x64.exe")) { throw 'installer\redist\windowsdesktop-runtime-win-x64.exe is missing (https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe)' }

# 1. чистая база курса: один файл, без WAL, без таблиц прогресса
Write-Host '-- sealing content\course.db'
dotnet run --project src\CsharpLearning.ContentBuilder -c Release -- "--seal=$root\content\course.db"
if ($LASTEXITCODE -ne 0) { throw 'course.db check failed' }

# 2. публикация программы (нужен .NET 10 Desktop Runtime у пользователя; ставится установщиком)
$app = Join-Path $PSScriptRoot 'app'
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
Write-Host '-- publish'
dotnet publish src\CsharpLearning.App -c Release -r win-x64 --self-contained false -o $app
if ($LASTEXITCODE -ne 0) { throw 'publish failed' }

# 3. в комплекте не должно быть чужого прогресса, настроек и журналов
foreach ($bad in 'progress.db', 'progress.db-wal', 'progress.db-shm', 'settings.json', 'course.db.prev') {
    Get-ChildItem $app -Recurse -Filter $bad -ErrorAction SilentlyContinue | Remove-Item -Force
}
$shipped = Join-Path $app 'content\course.db'
if (-not (Test-Path $shipped)) { throw 'content\course.db is missing from publish output' }
dotnet run --project src\CsharpLearning.ContentBuilder -c Release -- "--seal=$shipped"
if ($LASTEXITCODE -ne 0) { throw 'shipped course.db check failed' }
foreach ($side in '-wal', '-shm', '-journal') { if (Test-Path "$shipped$side") { throw "stray file $shipped$side" } }
if (-not (Test-Path (Join-Path $app 'refs\core')) -or -not (Test-Path (Join-Path $app 'refs\desktop'))) { throw 'refs\ missing: examples will not compile without SDK' }

# 4. установщик
Write-Host '-- inno setup'
& $iscc "/DAppVersion=$version" "/DSourceDir=$app" "$PSScriptRoot\CSharpCoach.iss"
if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' }
$setup = Join-Path $PSScriptRoot "Output\CSharpCoach-Setup-$version.exe"
if (-not (Test-Path $setup)) { throw "setup not found: $setup" }

# 5. блок "app" в manifest.json — по нему программа узнаёт о новой версии
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
$size = (Get-Item $setup).Length
$publish = Join-Path $root 'publish'
New-Item -ItemType Directory -Force $publish | Out-Null
Copy-Item $setup $publish -Force
$manifestPath = Join-Path $publish 'manifest.json'
if (Test-Path $manifestPath) { $m = ([IO.File]::ReadAllText($manifestPath).TrimStart([char]0xFEFF)) | ConvertFrom-Json } else { $m = [pscustomobject]@{ schema = 4 } }
$node = [pscustomobject]@{ version = $version; file = "CSharpCoach-Setup-$version.exe"; sha256 = $sha; size = $size }
if ($m.PSObject.Properties.Name -contains 'app') { $m.app = $node } else { $m | Add-Member -NotePropertyName app -NotePropertyValue $node }
[IO.File]::WriteAllText($manifestPath, ($m | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding $false))   # без BOM

Write-Host ''
Write-Host "OK  $setup"
Write-Host "    sha256 $sha, $([math]::Round($size / 1MB, 1)) MB"
Write-Host "    upload to hosting: publish\manifest.json, publish\CSharpCoach-Setup-$version.exe, publish\course-v*.db.gz"
