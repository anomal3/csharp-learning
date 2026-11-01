using System.IO;
using CsharpLearning.App.Services;
using CsharpLearning.Core.Analytics;
using CsharpLearning.Core.Model;
using CsharpLearning.Core.Storage;

namespace CsharpLearning.App;

/// <summary>
/// Живой хаб приложения: контент-база, прогресс-база, настройки, аналитика.
/// Всё офлайн; сеть нужна только когда пришла пора обновить уроки.
/// course.db можно закрыть на время обновления и открыть заново — прогресс (progress.db) не трогается.
/// </summary>
public sealed class LearningState : IDisposable
{
    ContentDb? _course;
    List<Chapter>? _chapters;
    Dictionary<int, string> _statuses = new();
    LearnerSummary? _summary;

    public ProgressDb Progress { get; }
    public AppSettings Settings { get; }

    /// <summary>Может быть null только на время подмены course.db обновлением.</summary>
    public ContentDb? Course => _course;

    public IReadOnlyList<Chapter> Chapters => _chapters ??= (_course?.LoadChapters() ?? new List<Chapter>());
    public Dictionary<int, string> Statuses => _statuses;
    public LearnerSummary Summary => _summary ??= AnalyticsEngine.Build(Progress, Chapters);

    public int ContentVersion => _course is not null && int.TryParse(_course.GetMeta("content_version"), out var v) ? v : 0;
    public string? BuiltAt => _course?.GetMeta("built_at");

    public static LearningState Create(string[] args)
    {
        Paths.Init(args);
        EnsureCourse();
        var settings = AppSettings.Load(Paths.SettingsFile);
        return new LearningState(settings);
    }

    /// <summary>Первый запуск: копируем офлайн-дистрибутив курса из папки программы в локальные данные.</summary>
    static void EnsureCourse()
    {
        bool exists = File.Exists(Paths.CourseDb);
        int localVersion = exists ? ContentVersionOf(Paths.CourseDb) : -1;

        string[] candidates =
        {
            Paths.BundledCourseDb,
            Path.Combine(Paths.AppBase, "course.db"),
            Path.Combine(Paths.AppBase, "..", "..", "..", "content", "course.db"),
        };

        foreach (var src in candidates)
        {
            try
            {
                if (!File.Exists(src)) continue;

                // База уже есть: подменяем её, только если вместе с новой версией программы приехал более свежий курс.
                // Прогресс (progress.db) лежит отдельно и остаётся нетронутым.
                if (exists)
                {
                    if (ContentVersionOf(src) <= localVersion) return;
                    foreach (var side in new[] { Paths.CourseDb + "-wal", Paths.CourseDb + "-shm" })
                        if (File.Exists(side)) File.Delete(side);
                    File.Copy(Paths.CourseDb, Paths.CourseDb + ".prev", overwrite: true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Paths.CourseDb))!);
                File.Copy(src, Paths.CourseDb, overwrite: true);
                return;
            }
            catch { /* пробуем следующий кандидат */ }
        }

        // Ни одной копии нет — создаём пустую базу: приложение запустится и предложит обновиться.
        using var db = new ContentDb(Paths.CourseDb);
    }

    /// <summary>Версия контента в файле базы; 0, если файл не читается или версии в нём нет.</summary>
    static int ContentVersionOf(string path)
    {
        try
        {
            using var db = new ContentDb(path);
            return int.TryParse(db.GetMeta("content_version"), out var v) ? v : 0;
        }
        catch { return 0; }
    }

    LearningState(AppSettings settings)
    {
        Progress = new ProgressDb(Paths.ProgressDb);
        Settings = settings;
        OpenCourse();
    }

    public void OpenCourse()
    {
        _course ??= new ContentDb(Paths.CourseDb);
        _chapters = null;
        _summary = null;
        _statuses = Progress.LoadStatuses();
    }

    /// <summary>Закрает course.db перед заменой файла обновлением (SQLite держит файл открытым).</summary>
    public void CloseCourse()
    {
        _course?.Dispose();
        _course = null;
        _chapters = null;
        _summary = null;
    }

    public Chapter? FindChapter(int id) => Chapters.FirstOrDefault(c => c.Id == id);

    public List<LessonRef> FlatLessons() => Chapters.SelectMany(c => c.Lessons).ToList();

    public Lesson? LoadLesson(int id) => _course?.LoadLesson(id);

    public int CountLessons() => _course?.CountLessons() ?? 0;
    public int CountQuestions() => _course?.CountQuestions() ?? 0;

    public string StatusOf(int lessonId) => _statuses.TryGetValue(lessonId, out var s) ? s : "";

    public void RefreshProgress()
    {
        _statuses = Progress.LoadStatuses();
        _summary = null;
    }

    public void SaveSettings() => Settings.Save(Paths.SettingsFile);

    public void Dispose()
    {
        SaveSettings();
        _course?.Dispose();
        Progress.Dispose();
    }
}
