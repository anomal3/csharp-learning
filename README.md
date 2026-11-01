# CSharpCoach

Офлайн-тренажёр C# для новичков (WPF, .NET 10): уроки с пояснениями-аналогиями, запуск примеров через Roslyn,
оконные примеры WinForms, редактор графов (визуальное программирование на C#), расширенный режим (IL и память).

## Структура
- `src/CsharpLearning.App` — приложение WPF.
- `src/CsharpLearning.Core` — модель, база уроков (`course.db`), прогресс (`progress.db`), запуск примеров, граф и формы.
- `src/CsharpLearning.GraphEditor` — редактор графов.
- `src/CsharpLearning.ContentBuilder` — сборка курса, `--seal=<course.db>` — подготовка чистой базы для установщика.
- `installer` — Inno Setup (`CSharpCoach.iss`) и `build.ps1`.
- `docs` — `PLAN.md`, `RELEASE.md` (выпуск и автообновление).

## Сборка
```
dotnet build CsharpLearning.slnx
```
Готовая база курса `content\course.db` в репозиторий не входит: возьмите её из Releases или соберите
`dotnet run --project src\CsharpLearning.ContentBuilder -- --offline --fresh` (нужен кэш страниц `content\raw`).

Установщик: `powershell -ExecutionPolicy Bypass -File installer\build.ps1` (см. `docs/RELEASE.md`;
нужны Inno Setup 6 и `installer\redist\windowsdesktop-runtime-win-x64.exe`).
