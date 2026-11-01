using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CsharpLearning.App.Services;
using CsharpLearning.App.View;
using CsharpLearning.Core.Analytics;
using CsharpLearning.Core.Model;
using CsharpLearning.Core.Runner;
using CsharpLearning.Core.Storage;

namespace CsharpLearning.App;

/// <summary>Строка оглавления: один урок в списке главы.</summary>
public sealed class LessonCard : NotifyBase
{
    public int Id { get; init; }
    public int ChapterId { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public int ReadMinutes { get; init; }
    public int CodeCount { get; init; }
    public int QuestionCount { get; init; }
    public List<string> Tags { get; init; } = new();

    /// <summary>Для группировки списка уроков по главам в оглавлении.</summary>
    public int ChapterNumber { get; init; }
    public string ChapterTitle { get; init; } = "";
    public string ChapterProgress { get; init; } = "";

    /// <summary>Объект-ключ группы: заголовок главы рисуется один раз над её уроками.</summary>
    public ChapterGroup Group => new(ChapterNumber, ChapterTitle, ChapterProgress);

    public sealed record ChapterGroup(int Number, string Title, string Progress);

    string _status = "";
    public string Status { get => _status; set => Set(ref _status, value); }

    public string Mark => Status switch
    {
        "done" => "✓",
        "reading" => "…",
        _ => "",
    };

    public bool IsRead => Status == "done";
    public bool HasQuestions => QuestionCount > 0;
    public string ShortTitle => Title.Length <= 52 ? Title : Title[..49] + "…";
    public string MetaText => $"{ReadMinutes} мин · {QuestionCount} вопр.";
}

/// <summary>Узел оглавления: глава со списком уроков.</summary>
public sealed class ChapterNode : NotifyBase
{
    public Chapter Model { get; }
    public ObservableCollection<LessonCard> Lessons { get; } = new();

    public ChapterNode(Chapter chapter)
    {
        Model = chapter;
        foreach (var l in chapter.Lessons)
            Lessons.Add(new LessonCard
            {
                Id = l.Id,
                ChapterId = l.ChapterId,
                Slug = l.Slug,
                Title = l.Title,
                ReadMinutes = l.ReadMinutes,
                CodeCount = l.CodeCount,
                QuestionCount = l.QuestionCount,
                Tags = l.Tags,
                ChapterNumber = chapter.Number,
                ChapterTitle = chapter.Title,
                ChapterProgress = chapter.Lessons.Count + " ур.",
            });
    }

    public int Number => Model.Number;
    public string Title => Model.Title;

    bool _expanded;
    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }

    public int ReadCount => Lessons.Count(l => l.IsRead);
    public string ProgressText => $"{ReadCount} / {Lessons.Count}";
    public double ProgressPercent => Lessons.Count == 0 ? 0 : 100.0 * ReadCount / Lessons.Count;

    public void ApplyStatuses(Dictionary<int, string> statuses)
    {
        foreach (var card in Lessons)
            card.Status = statuses.TryGetValue(card.Id, out var s) ? s : "";
        Raise(nameof(ReadCount));
        Raise(nameof(ProgressText));
        Raise(nameof(ProgressPercent));
    }
}

/// <summary>
/// Главное представление приложения: оглавление слева, урок по центру,
/// запуск примеров, аналитика, расширенный режим и обновление контента.
/// </summary>
public sealed class MainViewModel : NotifyBase, IDisposable
{
    readonly LearningState _state;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainViewModel(LearningState state)
    {
        _state = state;
        Advanced = new AdvancedViewModel(state);

        OpenLessonCommand = new RelayCommand(() => OpenLesson(SelectedCard), () => SelectedCard is not null);
        NextLessonCommand = new RelayCommand(OpenNextLesson, () => CurrentLesson is not null);
        PrevLessonCommand = new RelayCommand(OpenPreviousLesson, () => CurrentLesson is not null);
        RunExampleCommand = new RelayCommand(async () => await RunCurrentExampleAsync(), () => !IsRunning && CurrentQuestion is not null);
        SubmitAnswerCommand = new RelayCommand(() => SubmitCurrentAnswer(), () => CanSubmitAnswer);
        ResetAnswerCommand = new RelayCommand(ResetCurrentAnswer, () => CurrentQuestion?.Answered == true);
        ShowIlCommand = new RelayCommand(async () => await AnalyzeCurrentAsync(), () => CurrentQuestion?.HasCode == true);
        NextQuestionCommand = new RelayCommand(NextQuestion, () => CurrentLesson != null);
        PrevQuestionCommand = new RelayCommand(PrevQuestion, () => CurrentLesson != null);
        SelectOptionCommand = new RelayCommand(o =>
        {
            var q = CurrentQuestion;
            if (q is null || q.Answered) return;
            q.SelectedOption = o as string ?? "";
            RefreshStatuses();
            Raise(nameof(CanSubmitAnswer));
        });
        ShowDashboardCommand = new RelayCommand(() => { ShowAdvanced = false; ShowDashboard = true; RebuildDashboard(); });
        CloseOverlayCommand = new RelayCommand(() => { ShowDashboard = false; ShowAdvanced = false; });
        OpenFirstLessonCommand = new RelayCommand(OpenNextLesson);
        CheckUpdateCommand = new RelayCommand(async () => await CheckUpdateAsync(install: false));
        InstallUpdateCommand = new RelayCommand(async () => await CheckUpdateAsync(install: true), () => !IsUpdating);
        ResetProgressCommand = new RelayCommand(ResetProgress);
        ClearRunCommand = new RelayCommand(() => { RunOutput = ""; RunErrors = ""; HasRunResult = false; RunStatus = ""; });
        ToggleAdvancedCommand = new RelayCommand(async () =>
        {
            ShowDashboard = false;
            ShowAdvanced = !ShowAdvanced;
            if (ShowAdvanced) await AnalyzeCurrentAsync();
            _state.Settings.AdvancedMode = ShowAdvanced;
            _state.SaveSettings();
        });

        _tick.Tick += (_, _) => CurrentLesson?.Tick();
        _tick.Start();

        RebuildChapters();
        RebuildDashboard();
        StatusLine = BuildStatusLine();
    }

    // ---------------- оглавление ----------------
    public ObservableCollection<ChapterNode> Chapters { get; } = new();
    public ObservableCollection<LessonCard> LessonCards { get; } = new();

    void RebuildChapters()
    {
        Chapters.Clear();
        foreach (var ch in _state.Chapters)
        {
            var node = new ChapterNode(ch);
            node.ApplyStatuses(_state.Statuses);
            Chapters.Add(node);
        }
        ApplyFilter();
    }

    string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) ApplyFilter(); }
    }

    public bool HasSearch => !string.IsNullOrWhiteSpace(_search);

    void ApplyFilter()
    {
        LessonCards.Clear();
        var q = _search.Trim();

        foreach (var chapter in Chapters)
        {
            List<LessonCard> matches = string.IsNullOrEmpty(q)
                ? chapter.Lessons.ToList()
                : chapter.Lessons.Where(l =>
                    l.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    l.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();

            if (matches.Count == 0) continue;

            foreach (var card in matches) LessonCards.Add(card);
        }
    }

    LessonCard? _selectedCard;
    public LessonCard? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (Set(ref _selectedCard, value))
            {
                Raise(nameof(CanSubmitAnswer));
                Raise(nameof(SelectedCardTitle));
            }
        }
    }

    public string SelectedCardTitle => SelectedCard?.Title ?? "";

    ChapterNode? _selectedChapter;
    public ChapterNode? SelectedChapter { get => _selectedChapter; set => Set(ref _selectedChapter, value); }

    // ---------------- урок ----------------
    LessonViewModel? _lesson;
    public LessonViewModel? CurrentLesson
    {
        get => _lesson;
        private set
        {
            if (Set(ref _lesson, value))
            {
                Raise(nameof(HasLesson));
                Raise(nameof(CurrentQuestion));
                Raise(nameof(CanSubmitAnswer));
                Raise(nameof(ShowQuestionPanel));
                Raise(nameof(NextHint));
            }
        }
    }

    public bool HasLesson => CurrentLesson is not null;
    public bool NoLesson => CurrentLesson is null;

    public QuestionViewModel? CurrentQuestion => CurrentLesson?.CurrentQuestion;
    public bool ShowQuestionPanel => CurrentQuestion is not null && !ShowDashboard && !ShowAdvanced;
    public bool CanSubmitAnswer => CurrentQuestion is not null && !CurrentQuestion.Answered &&
                                   !string.IsNullOrWhiteSpace(CurrentQuestion.Given);
    public string NextHint => CurrentLesson?.NextLessonRef()?.Title ?? "это последний урок курса";

    public void OpenLesson(LessonCard? card)
    {
        if (card is null) return;
        var model = _state.LoadLesson(card.Id);
        if (model is null) { StatusLine = "Урок не найден в локальной базе — попробуйте обновить контент."; return; }

        CurrentLesson?.Dispose();
        var chapter = _state.FindChapter(card.ChapterId);
        CurrentLesson = new LessonViewModel(_state, model, chapter);

        ShowDashboard = false;
        ShowAdvanced = false;
        RunOutput = ""; HasRunResult = false; RunErrors = "";

        var node = Chapters.FirstOrDefault(c => c.Model.Id == card.ChapterId);
        if (node is not null && !node.IsExpanded) node.IsExpanded = true;

        Raise(nameof(CurrentQuestion));
        Raise(nameof(ShowQuestionPanel));
        Raise(nameof(CanSubmitAnswer));
        _state.Progress.Touch("lesson_view", model.Slug);
    }

    public void OpenLessonById(int id) => OpenLesson(LessonCards.FirstOrDefault(c => c.Id == id));

    public void OpenNextLesson()
    {
        var next = CurrentLesson?.NextLessonRef();
        if (next is not null) { OpenLessonById(next.Id); return; }

        var unread = LessonCards.FirstOrDefault(c => c.Status != "done") ?? LessonCards.FirstOrDefault();
        OpenLesson(unread);
    }

    void OpenPreviousLesson()
    {
        if (CurrentLesson is null) return;
        int pos = LessonCards.ToList().FindIndex(c => c.Id == CurrentLesson.Model.Id);
        if (pos <= 0) return;
        OpenLesson(LessonCards.ToList()[pos - 1]);
    }

    // ---------------- вопросы ----------------
    public void SubmitCurrentAnswer()
    {
        var q = CurrentQuestion;
        var lesson = CurrentLesson;
        if (q is null || lesson is null) return;

        lesson.SubmitAnswer(q, q.Given);
        Raise(nameof(CanSubmitAnswer));
        Raise(nameof(NextHint));
        RefreshStatuses();
    }

    public void ResetCurrentAnswer()
    {
        CurrentQuestion?.ResetForRetry();
        Raise(nameof(CanSubmitAnswer));
    }

    public void NextQuestion() { CurrentLesson?.NextQuestion(); Raise(nameof(CurrentQuestion)); Raise(nameof(CanSubmitAnswer)); Raise(nameof(ShowQuestionPanel)); }
    public void PrevQuestion() { CurrentLesson?.PrevQuestion(); Raise(nameof(CurrentQuestion)); Raise(nameof(CanSubmitAnswer)); Raise(nameof(ShowQuestionPanel)); }

    void RefreshStatuses()
    {
        _state.RefreshProgress();
        foreach (var node in Chapters) node.ApplyStatuses(_state.Statuses);
    }

    // ---------------- запуск примера ----------------
    bool _isRunning;
    public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value); }

    string _runOutput = "";
    public string RunOutput { get => _runOutput; private set => Set(ref _runOutput, value); }

    string _runErrors = "";
    public string RunErrors { get => _runErrors; private set => Set(ref _runErrors, value); }

    bool _hasRunResult;
    public bool HasRunResult { get => _hasRunResult; private set => Set(ref _hasRunResult, value); }

    string _runStatus = "";
    public string RunStatus { get => _runStatus; private set => Set(ref _runStatus, value); }

    public string RunTitle => CurrentQuestion?.IsGui == true
        ? $"Запуск {CurrentQuestion.GuiFlavor}-примера: окно появится на экране и закроется само"
        : "Запуск примера в отдельном процессе (песочница убьёт бесконечный цикл через 8 с)";

    public async Task RunCurrentExampleAsync()
    {
        var q = CurrentQuestion;
        if (q is null || string.IsNullOrWhiteSpace(q.Code)) { RunStatus = "В этом примере нет кода для запуска."; return; }

        IsRunning = true;
        HasRunResult = false;
        RunOutput = "";
        RunErrors = "";
        RunStatus = q.IsGui ? "Собираю и открываю окно…" : "Собираю и запускаю…";

        string workDir = Path.Combine(Paths.RunWork, "run_" + Guid.NewGuid().ToString("N"));

        try
        {
            var compiled = await Task.Run(() => IlWorkbench.Compile(q.Code, workDir, "lesson_demo"));

            if (!compiled.Success)
            {
                RunErrors = compiled.Diagnostics;
                RunStatus = "Компилятор отверг пример: " + FirstLine(compiled.Diagnostics);
                HasRunResult = true;
                Raise(nameof(HasRunResult));
                return;
            }

            int timeout = q.IsGui ? 15000 : 8000;
            var run = await Task.Run(() => SandboxRunner.Run(compiled.AssemblyPath!, timeout, workDir));

            RunOutput = string.IsNullOrWhiteSpace(run.Output) ? "(программа ничего не вывела)" : run.Output.Trim();
            if (!string.IsNullOrWhiteSpace(run.Error)) RunErrors = run.Error.Trim();

            RunStatus = run.TimedOut
                ? "Программа не завершилась за " + timeout + " мс — песочница остановила её."
                : $"Выполнено за {run.ElapsedMs:F0} мс · код возврата {run.ExitCode} · {(run.Success ? "успешно" : "с ошибкой")}";
        }
        catch (Exception ex)
        {
            RunErrors = ex.Message;
            RunStatus = "Сбой запуска: " + ex.Message;
        }
        finally
        {
            HasRunResult = true;
            IsRunning = false;
            TryCleanup(workDir);
            Raise(nameof(HasRunResult));
            Raise(nameof(RunTitle));
        }
    }

    // ---------------- расширенный режим ----------------
    public AdvancedViewModel Advanced { get; }

    bool _showAdvanced;
    public bool ShowAdvanced
    {
        get => _showAdvanced;
        set { if (Set(ref _showAdvanced, value)) { Raise(nameof(ShowQuestionPanel)); Raise(nameof(ShowDashboard)); } }
    }

    public async Task AnalyzeCurrentAsync()
    {
        var q = CurrentQuestion;
        if (q is null || !q.HasCode) { return; }
        await Advanced.AnalyzeAsync(q.Code);
    }

    // ---------------- аналитика ----------------
    public LearnerSummary Summary => _state.Summary;

    bool _showDashboard;
    public bool ShowDashboard
    {
        get => _showDashboard;
        set { if (Set(ref _showDashboard, value)) { Raise(nameof(ShowQuestionPanel)); Raise(nameof(ShowAdvanced)); } }
    }

    public bool ShowDashboardNow => ShowDashboard;

    public ObservableCollection<ConceptStat> WeakConcepts { get; } = new();
    public ObservableCollection<ConceptStat> GoodConcepts { get; } = new();
    public ObservableCollection<ChapterStat> ChapterStats { get; } = new();
    public ObservableCollection<AnswerLog> RecentAnswers { get; } = new();

    public void RebuildDashboard()
    {
        var s = _state.Summary;
        WeakConcepts.Clear();
        foreach (var c in s.Concepts.Where(c => c.Health is Health.Struggling or Health.Normal).Take(12))
            WeakConcepts.Add(c);

        GoodConcepts.Clear();
        foreach (var c in s.Concepts.Where(c => c.Health == Health.Good).Take(12))
            GoodConcepts.Add(c);

        ChapterStats.Clear();
        foreach (var c in s.Chapters) ChapterStats.Add(c);

        RecentAnswers.Clear();
        foreach (var a in _state.Progress.LoadAnswers(take: 14)) RecentAnswers.Add(a);

        Raise(nameof(Summary));
        Raise(nameof(TotalMinutesText));
        Raise(nameof(AccuracyText));
        Raise(nameof(OverallProgressText));
    }

    public string TotalMinutesText => $"{Summary.TotalMinutes:F1} мин чтения";
    public string AccuracyText => Summary.QuestionsAnswered == 0
        ? "ответов пока нет"
        : $"точность {Summary.Accuracy:P0} ({Summary.QuestionsCorrect}/{Summary.QuestionsAnswered})";
    public string OverallProgressText => $"{Summary.LessonsRead} из {Summary.LessonsTotal} уроков ({Summary.OverallProgressPercent:F0}%)";

    void ResetProgress()
    {
        _state.Progress.ResetProgress();
        _state.RefreshProgress();
        RefreshStatuses();
        RebuildDashboard();
        StatusLine = "Прогресс сброшен: история чтения и ответов очищена (контент цел).";
    }

    // ---------------- обновление контента ----------------
    bool _isUpdating;
    public bool IsUpdating { get => _isUpdating; private set => Set(ref _isUpdating, value); }

    string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }

    public async Task CheckUpdateAsync(bool install)
    {
        IsUpdating = true;
        UpdateStatus = install ? "Скачиваю обновлённый курс…" : "Проверяю версию курса на хостинге…";

        CurrentLesson?.Dispose();
        CurrentLesson = null;

        try
        {
            var known = Math.Max(_state.ContentVersion, _state.Settings.KnownContentVersion);

            // Устанавливаем только с закрытой базой: SQLite держит course.db открытым,
            // и File.Replace над открытым файлом не проходит.
            if (install) _state.CloseCourse();

            var result = await UpdateService.CheckAndInstallAsync(known, install);
            UpdateStatus = result.Message;
            _state.Settings.LastUpdateCheck = DateTime.UtcNow;

            if (result.Updated)
            {
                _state.Settings.KnownContentVersion = result.Info?.ContentVersion ?? known;
                _state.OpenCourse();
                CurrentLesson?.Dispose();
                CurrentLesson = null;
                RebuildChapters();
                RebuildDashboard();
                StatusLine = BuildStatusLine();
            }
            else if (result.Info is not null)
            {
                _state.Settings.KnownContentVersion = Math.Max(_state.Settings.KnownContentVersion, result.Info.ContentVersion);
            }

            _state.SaveSettings();
        }
        catch (Exception ex)
        {
            UpdateStatus = "Обновление не удалось: " + ex.Message;
        }
        finally
        {
            // при любом исходе (нет обновления, сбой) база должна быть снова открыта
            if (install && _state.Course is null) _state.OpenCourse();
            IsUpdating = false;
            Raise(nameof(UpdateStatus));
        }
    }

    /// <summary>Кнопка «Проверить обновление»: если на хостинге курс новее — сразу ставим; прогресс не трогается.</summary>
    public async Task<string> CheckAndUpdateContentAsync()
    {
        var known = Math.Max(_state.ContentVersion, _state.Settings.KnownContentVersion);
        var probe = await UpdateService.CheckAndInstallAsync(known, install: false);
        if (probe.Ok && probe.Info is not null && probe.Info.IsNewerThan(known))
            await CheckUpdateAsync(install: true);
        else
        {
            UpdateStatus = probe.Message;
            _state.Settings.LastUpdateCheck = DateTime.UtcNow;
            _state.SaveSettings();
        }
        return UpdateStatus;
    }

    public bool AutoUpdateEnabled => _state.Settings.AutoUpdate;

    public async Task CheckUpdateIfDueAsync()
    {
        if (!_state.Settings.AutoUpdate) return;
        if (_state.Settings.LastUpdateCheck is not null &&
            DateTime.UtcNow - _state.Settings.LastUpdateCheck < TimeSpan.FromDays(3)) return;

        await CheckUpdateAsync(install: false);
    }

    // ---------------- статусная строка ----------------
    string _statusLine = "";
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }

    string BuildStatusLine()
    {
        int lessons = _state.CountLessons();
        int questions = _state.CountQuestions();
        string built = _state.BuiltAt is null ? "—" : ShortDate(_state.BuiltAt);
        return $"Офлайн-база: {lessons} уроков, {questions} вопросов · версия контента {_state.ContentVersion} · собрана {built}";
    }

    static string ShortDate(string iso) =>
        DateTime.TryParse(iso, out var dt) ? dt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : iso;

    static string FirstLine(string s) => (s ?? "").Split('\n').FirstOrDefault() ?? "";

    static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* следующая сборка уберёт */ }
    }

    // ---------------- команды ----------------
    public RelayCommand OpenLessonCommand { get; }
    public RelayCommand NextLessonCommand { get; }
    public RelayCommand PrevLessonCommand { get; }
    public RelayCommand RunExampleCommand { get; }
    public RelayCommand SubmitAnswerCommand { get; }
    public RelayCommand ResetAnswerCommand { get; }
    public RelayCommand ShowIlCommand { get; }
    public RelayCommand NextQuestionCommand { get; }
    public RelayCommand PrevQuestionCommand { get; }
    public RelayCommand SelectOptionCommand { get; }
    public RelayCommand ShowDashboardCommand { get; }
    public RelayCommand CloseOverlayCommand { get; }
    public RelayCommand OpenFirstLessonCommand { get; }
    public RelayCommand CheckUpdateCommand { get; }
    public RelayCommand InstallUpdateCommand { get; }
    public RelayCommand ResetProgressCommand { get; }
    public RelayCommand ClearRunCommand { get; }
    public RelayCommand ToggleAdvancedCommand { get; }

    public void Dispose()
    {
        _tick.Stop();
        CurrentLesson?.Dispose();
    }
}
