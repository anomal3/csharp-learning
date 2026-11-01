namespace CsharpLearning.Core.Model;

/// <summary>Набор (глава) учебника.</summary>
public sealed class Chapter
{
    public int Id { get; init; }
    public int Number { get; init; }
    public string Title { get; init; } = "";
    public string Course { get; init; } = "tutorial";
    public List<LessonRef> Lessons { get; init; } = new();
}

public sealed class LessonRef
{
    public int Id { get; init; }
    public int ChapterId { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public int Order { get; init; }
    public int WordCount { get; init; }
    public int ReadMinutes { get; init; }
    public int CodeCount { get; init; }
    public int QuestionCount { get; init; }
    /// <summary>Учебные теги урока (loops, oop, async…) — по ним считается карта слабых тем.</summary>
    public List<string> Tags { get; init; } = new();

    public string ShortTitle => Title.Length <= 58 ? Title : Title[..55] + "…";
}

public sealed class Lesson
{
    public int Id { get; init; }
    public int ChapterId { get; init; }
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Url { get; init; } = "";
    public string UpdatedAt { get; init; } = "";
    public int Order { get; init; }
    public int WordCount { get; init; }
    public int ReadMinutes { get; init; }
    public string SourceHash { get; init; } = "";
    public List<string> Tags { get; init; } = new();
    public List<ContentBlock> Blocks { get; init; } = new();
    public List<Explanation> Explanations { get; init; } = new();
    public List<QuizItem> Questions { get; set; } = new();

    public IEnumerable<ContentBlock> CodeBlocks => Blocks.Where(b => b.Kind == BlockKind.Code);
}

public enum BlockKind { Heading, Paragraph, Code, List, Table, Note }

public sealed class ContentBlock
{
    public int Index { get; init; }
    public BlockKind Kind { get; init; }
    public string Text { get; init; } = "";
    public string? Code { get; init; }
    public int LineStart { get; init; }
}

/// <summary>Не сухая теория, а человеческое пояснение к уроку.</summary>
public enum ExplanationKind { Analogy, Everyday, Bridge, Why, Mistake, CheatSheet, WhereUsed }

public sealed class Explanation
{
    public string Title { get; init; } = "";
    public ExplanationKind Kind { get; init; }
    public string Body { get; init; } = "";
}

public enum QuizFormat { MultipleChoice, OpenText, PredictOutput }

public sealed record QuizItem
{
    public int Id { get; init; }
    public int LessonId { get; set; }
    public int Index { get; set; }
    public QuizFormat Format { get; init; }
    public string Prompt { get; init; } = "";
    public string? Code { get; init; }
    /// <summary>Для GUI-примеров: консольный эквивалент, по которому проверяется эталон.</summary>
    public string? HarnessCode { get; init; }
    public List<string> Options { get; init; } = new();
    public string CorrectAnswer { get; init; } = "";
    public string Explanation { get; init; } = "";
    public bool FromSource { get; init; }
    public string? ConceptTag { get; init; }
    /// <summary>GUI-пример (WinForms/WPF), а не консольный.</summary>
    public bool IsGui { get; set; }
    public string GuiFlavor { get; set; } = "";
}
