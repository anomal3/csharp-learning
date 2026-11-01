using System.Diagnostics;
using System.IO;
using System.Windows;
using CsharpLearning.App.Services;
using CsharpLearning.App.View;
using CsharpLearning.Core.Model;
using CsharpLearning.Core.Pedagogy;
using CsharpLearning.Core.Runner;

namespace CsharpLearning.App;

/// <summary>Слово в тексте урока, над которым при наведении мыши всплывает аналогия.</summary>
public sealed record GlossaryHit(int Start, int Length, string Concept, string Animal, string Everyday, string Bridge);

/// <summary>Блок урока для показа: сам блок плюс найденные в нём «слова-подсказки».</summary>
public sealed class BlockVm
{
    public BlockVm(ContentBlock b) { Block = b; }
    public ContentBlock Block { get; }
    public BlockKind Kind => Block.Kind;
    public string Text => Block.Text;
    public string? Code => Block.Code;
    public List<GlossaryHit> Hits { get; } = new();
}

/// <summary>
/// Представление одного урока: текст, пояснения, примеры-вопросы внизу,
/// учёт времени чтения и проверка ответов.
/// </summary>
public sealed class LessonViewModel : NotifyBase, IDisposable
{
    readonly LearningState _state;
    readonly Stopwatch _readClock = new();
    int _sessionId;
    bool _closed;

    public Lesson Model { get; }
    public Chapter? Chapter { get; }

    public LessonViewModel(LearningState state, Lesson lesson, Chapter? chapter)
    {
        _state = state;
        Model = lesson;
        Chapter = chapter;

        _sessionId = state.Progress.OpenLesson(lesson.Id, lesson.Slug, lesson.Title, chapter?.Number ?? 0);
        _readClock.Start();
        state.Progress.Touch("open_lesson", lesson.Slug);

        foreach (var q in lesson.Questions)
            Questions.Add(new QuestionViewModel(this, q, answeredBefore: state.Progress.HasAnswered(q.Id)));

        // Пример «внизу урока»: первый непройденный вопрос этого урока
        _currentQuestionIndex = IndexOfFirstUnanswered();

        Blocks = lesson.Blocks.Select(b => new BlockVm(b)).ToList();
        BuildGlossary();
    }

    public List<BlockVm> Blocks { get; }

    public bool HasGlossary => Blocks.Any(b => b.Hits.Count > 0);

    /// <summary>
    /// Аналогии не свалены в кучу над уроком, а привязаны к самому слову: первое упоминание
    /// каждого понятия в тексте подчёркивается, а при наведении мыши появляется картинка из жизни.
    /// </summary>
    void BuildGlossary()
    {
        var concepts = Model.Explanations
            .Where(e => e.Kind == ExplanationKind.Analogy)
            .Select(e => e.Title.Split(" — ")[0])
            .Distinct();

        foreach (var concept in concepts)
        {
            var a = AnalogyLibrary.All.FirstOrDefault(x => x.Concept == concept);
            if (a is null) continue;

            var terms = a.Keywords.Select(k => k.Trim()).Where(k => k.Length >= 4)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(k => k.Length)
                .Select(System.Text.RegularExpressions.Regex.Escape).ToList();
            if (terms.Count == 0) continue;

            var re = new System.Text.RegularExpressions.Regex(
                @"(?<![\p{L}\d_])(" + string.Join("|", terms) + @")\p{L}*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

            foreach (var block in Blocks)
            {
                if (block.Kind is not (BlockKind.Paragraph or BlockKind.List) || block.Hits.Count >= 2) continue;
                var m = re.Match(block.Text);
                while (m.Success && block.Hits.Any(h => m.Index < h.Start + h.Length && h.Start < m.Index + m.Length))
                    m = m.NextMatch();
                if (!m.Success) continue;

                block.Hits.Add(new GlossaryHit(m.Index, m.Length, a.Concept, a.AnimalPicture, a.Everyday, a.Bridge));
                break;
            }
        }
    }

    public List<QuestionViewModel> Questions { get; } = new();

    int _currentQuestionIndex;
    public int CurrentQuestionIndex
    {
        get => _currentQuestionIndex;
        set
        {
            if (Set(ref _currentQuestionIndex, Math.Clamp(value, 0, Math.Max(0, Questions.Count - 1))))
            {
                Raise(nameof(CurrentQuestion));
                Raise(nameof(HasQuestions));
                Raise(nameof(QuestionPosition));
            }
        }
    }

    int IndexOfFirstUnanswered()
    {
        for (int i = 0; i < Questions.Count; i++)
            if (!Questions[i].Answered) return i;
        return 0;
    }

    public QuestionViewModel? CurrentQuestion =>
        Questions.Count == 0 ? null : Questions[Math.Clamp(_currentQuestionIndex, 0, Questions.Count - 1)];

    public bool HasQuestions => Questions.Count > 0;
    public string QuestionPosition => Questions.Count == 0 ? "—" : $"{_currentQuestionIndex + 1} / {Questions.Count}";

    // ---------------- метаданные для шапки ----------------
    public string Title => Model.Title;
    public string ChapterTitle => Chapter?.Title ?? "";
    public int ChapterNumber => Chapter?.Number ?? 0;
    public string Url => Model.Url;
    public int WordCount => Model.WordCount;
    public int EstimatedMinutes => Model.ReadMinutes;
    public int CodeCount => Model.CodeBlocks.Count();
    public int QuestionCount => Model.Questions.Count;

    public IEnumerable<BlockVm> TextBlocks => Blocks;
    public IEnumerable<Explanation> Explanations => Model.Explanations;

    /// <summary>Пояснения-аналогии: то, что превращает теорию в понятное.</summary>
    public IEnumerable<Explanation> AnalogyBlocks =>
        Model.Explanations.Where(e => e.Kind is ExplanationKind.Analogy or ExplanationKind.Everyday or ExplanationKind.Bridge);

    public IEnumerable<Explanation> TrapBlocks =>
        Model.Explanations.Where(e => e.Kind == ExplanationKind.Mistake);

    public bool HasTraps => Model.Explanations.Any(e => e.Kind == ExplanationKind.Mistake);
    public bool HasAnalogy => Model.Explanations.Any(e => e.Kind is ExplanationKind.Analogy or ExplanationKind.Everyday or ExplanationKind.Bridge);

    // ---------------- время чтения ----------------
    public double SecondsRead => _readClock.Elapsed.TotalSeconds;

    public string ReadTimeText
    {
        get
        {
            var ts = TimeSpan.FromSeconds(_readClock.Elapsed.TotalSeconds);
            return ts.TotalMinutes >= 1 ? $"{(int)ts.TotalMinutes} мин {ts.Seconds} с" : $"{ts.Seconds} с";
        }
    }

    public void Tick() => Raise(nameof(ReadTimeText));

    void Close()
    {
        if (_closed) return;
        _closed = true;
        _readClock.Stop();
        _state.Progress.CloseLesson(_sessionId, _readClock.Elapsed.TotalSeconds, Model.WordCount, finished: true);
        _state.Progress.Touch("close_lesson", Model.Slug);
        _state.RefreshProgress();
    }

    public void Dispose() => Close();

    // ---------------- ответы на вопросы ----------------
    public void SubmitAnswer(QuestionViewModel q, string given)
    {
        var sw = Stopwatch.StartNew();
        bool correct = q.CheckCorrect(given);
        sw.Stop();

        _state.Progress.SaveAnswer(
            Model.Id, Model.Slug, q.Model.Id, q.Model.ConceptTag, q.Model.Prompt,
            given, q.Model.CorrectAnswer, correct, (int)sw.ElapsedMilliseconds, q.Model.IsGui);

        q.MarkAnswered(given, correct);
        _state.RefreshProgress();
        Raise(nameof(AnsweredCount));
        Raise(nameof(CorrectCount));
        Raise(nameof(QuestionPosition));
    }

    public int AnsweredCount => Questions.Count(q => q.Answered);
    public int CorrectCount => Questions.Count(q => q.IsCorrect);

    public void NextQuestion() => CurrentQuestionIndex = _currentQuestionIndex + 1;
    public void PrevQuestion() => CurrentQuestionIndex = _currentQuestionIndex - 1;
    public void JumpToQuestion(int i) => CurrentQuestionIndex = i;

    /// <summary>Поток: следующий урок той же главы, иначе — первая строка следующей главы.</summary>
    public LessonRef? NextLessonRef()
    {
        if (Chapter is null) return null;
        int pos = Chapter.Lessons.FindIndex(l => l.Id == Model.Id);
        if (pos >= 0 && pos + 1 < Chapter.Lessons.Count) return Chapter.Lessons[pos + 1];

        var nextChapter = _state.Chapters.FirstOrDefault(c => c.Number > Chapter.Number);
        return nextChapter?.Lessons.FirstOrDefault();
    }
}

/// <summary>Один вопрос «пример + что выведет» внизу урока.</summary>
public sealed class QuestionViewModel : NotifyBase
{
    readonly LessonViewModel _owner;

    public QuizItem Model { get; }

    public QuestionViewModel(LessonViewModel owner, QuizItem model, bool answeredBefore)
    {
        _owner = owner;
        Model = model;
        _answered = answeredBefore;
    }

    public string Prompt => Model.Prompt;
    public string Code => Model.Code ?? "";
    public bool HasCode => !string.IsNullOrWhiteSpace(Model.Code);
    public bool IsGui => Model.IsGui;
    public string GuiFlavor => Model.GuiFlavor;
    public string Concept => Model.ConceptTag ?? "";
    public bool FromSource => Model.FromSource;
    public bool IsChoice => Model.Format == QuizFormat.MultipleChoice && Model.Options.Count >= 2;
    public List<string> Options => Model.Options;
    public QuizFormat Format => Model.Format;

    string _given = "";
    public string Given { get => _given; set => Set(ref _given, value); }

    public string? SelectedOption
    {
        get => _given;
        set
        {
            if (Set(ref _given, value ?? "") && Answered == false)
                _owner.SubmitAnswer(this, _given);
        }
    }

    bool _answered;
    public bool Answered { get => _answered; private set => Set(ref _answered, value); }

    bool _isCorrect;
    public bool IsCorrect { get => _isCorrect; private set => Set(ref _isCorrect, value); }

    public bool ShowFeedback => Answered;
    public string FeedbackTitle => Answered ? (IsCorrect ? "Верно" : "Неверно") : "";
    public string ExplanationText => Model.Explanation;
    public string CorrectAnswerText => Model.CorrectAnswer;

    public bool CheckCorrect(string given)
    {
        if (string.IsNullOrWhiteSpace(given)) return false;

        if (IsChoice)
            return string.Equals(given.Trim(), Model.CorrectAnswer.Trim(), StringComparison.OrdinalIgnoreCase);

        // «Что выведет»: сравниваем построчно, игнорируя лишние пробелы и регистр
        static string Norm(string s) =>
            string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim())).Trim();

        var expected = Norm(Model.CorrectAnswer);
        var actual = Norm(given);

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) return true;

        // допускаем ответ «одной строкой», если ученик не переносил строки
        var expectedFlat = string.Join(" ", expected.Split('\n').Select(l => l.Trim())).Trim();
        return string.Equals(actual, expectedFlat, StringComparison.OrdinalIgnoreCase);
    }

    public void MarkAnswered(string given, bool correct)
    {
        _given = given;
        IsCorrect = correct;
        Answered = true;
        Raise(nameof(ShowFeedback));
        Raise(nameof(FeedbackTitle));
        Raise(nameof(Given));
    }

    public void ResetForRetry()
    {
        Answered = false;
        IsCorrect = false;
        Given = "";
        Raise(nameof(ShowFeedback));
        Raise(nameof(FeedbackTitle));
    }
}
