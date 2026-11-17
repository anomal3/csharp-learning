using CsharpLearning.Core.Runner;

namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Всё, чем курс одного языка отличается от другого: где лежат базы, откуда обновляться,
/// учебный план, аналогии, теги, раскраска кода. ContentBuilder и приложение работают с профилем,
/// а не с жёстко прописанным C#.
/// </summary>
public sealed class CourseProfile
{
    /// <summary>«csharp» или «rust» — так же называется ключ в настройках и аргументах (--lang=rust).</summary>
    public required string Id { get; init; }

    /// <summary>Название языка для окон и подписей.</summary>
    public required string Language { get; init; }

    /// <summary>Значение колонки course в базе (chapters/lessons).</summary>
    public required string CourseKey { get; init; }

    /// <summary>Каталог курса на metanit.com.</summary>
    public required string BaseUrl { get; init; }

    public required string DbFile { get; init; }
    public required string ProgressFile { get; init; }
    public required string ManifestFile { get; init; }

    /// <summary>Префикс пакета для хостинга: course-v2.db.gz / course-rust-v1.db.gz.</summary>
    public required string PackPrefix { get; init; }

    /// <summary>Папка кэша скачанных страниц внутри content/.</summary>
    public required string CacheDir { get; init; }

    public required IReadOnlyList<Curriculum.Stage> Stages { get; init; }
    public required Curriculum.Stage ExtraStage { get; init; }
    public required Func<string, bool> IsAuthored { get; init; }
    public required IReadOnlyList<AuthoredLessons.Authored> Authored { get; init; }

    public required IReadOnlyList<string> AllTags { get; init; }
    public required Func<string, string, List<string>> TagsOf { get; init; }

    public required IReadOnlyList<AnalogyLibrary.Analogy> Analogies { get; init; }
    public required Func<string, List<CodeSpan>> Classify { get; init; }

    /// <summary>Есть ли у курса расширенный режим (IL и карта памяти), редактор графов и оконные примеры — только у C#.</summary>
    public required bool HasDotNetTools { get; init; }

    public static readonly CourseProfile CSharp = new()
    {
        Id = "csharp",
        Language = "C#",
        CourseKey = "tutorial",
        BaseUrl = "https://metanit.com/sharp/tutorial/",
        DbFile = "course.db",
        ProgressFile = "progress.db",
        ManifestFile = "manifest.json",
        PackPrefix = "course-",
        CacheDir = "raw",
        Stages = Curriculum.Stages,
        ExtraStage = Curriculum.ExtraStage,
        IsAuthored = Curriculum.IsAuthored,
        Authored = AuthoredLessons.All,
        AllTags = TagMapper.All,
        TagsOf = TagMapper.Of,
        Analogies = AnalogyLibrary.All,
        Classify = CodeClassifier.Classify,
        HasDotNetTools = true,
    };

    public static readonly CourseProfile Rust = new()
    {
        Id = "rust",
        Language = "Rust",
        CourseKey = "rust",
        BaseUrl = "https://metanit.com/rust/tutorial/",
        DbFile = "course-rust.db",
        ProgressFile = "progress-rust.db",
        ManifestFile = "manifest-rust.json",
        PackPrefix = "course-rust-",
        CacheDir = "raw-rust",
        Stages = RustCurriculum.Stages,
        ExtraStage = RustCurriculum.ExtraStage,
        IsAuthored = RustCurriculum.IsAuthored,
        Authored = RustAuthoredLessons.All,
        AllTags = RustTagMapper.All,
        TagsOf = RustTagMapper.Of,
        Analogies = RustAnalogyLibrary.All,
        Classify = RustClassifier.Classify,
        HasDotNetTools = false,
    };

    public static IReadOnlyList<CourseProfile> All { get; } = new[] { CSharp, Rust };

    public static CourseProfile ById(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? CSharp;
}
