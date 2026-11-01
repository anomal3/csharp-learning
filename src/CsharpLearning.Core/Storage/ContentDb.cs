using System.Text.Json;
using CsharpLearning.Core.Model;
using Microsoft.Data.Sqlite;

namespace CsharpLearning.Core.Storage;

/// <summary>
/// База КОНТЕНТА (course.db): уроки, блоки текста, код, вопросы, пояснения.
/// Именно этот файл заменяется при самообновлении с хостинга — прогресс ученика в нём не хранится.
/// </summary>
public sealed class ContentDb : IDisposable
{
    public const int SchemaVersion = 4;

    readonly SqliteConnection _conn;
    public string FilePath { get; }

    public ContentDb(string path)
    {
        FilePath = path;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _conn = new SqliteConnection($"Data Source={path}");
        _conn.Open();
        Exec("""
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);

            CREATE TABLE IF NOT EXISTS chapters(
                id INTEGER PRIMARY KEY, course TEXT NOT NULL, number INTEGER NOT NULL,
                title TEXT NOT NULL, UNIQUE(course, number));

            CREATE TABLE IF NOT EXISTS lessons(
                id INTEGER PRIMARY KEY, course TEXT NOT NULL, chapter_id INTEGER NOT NULL REFERENCES chapters(id),
                slug TEXT NOT NULL, title TEXT NOT NULL, url TEXT NOT NULL, updated_at TEXT,
                ord INTEGER NOT NULL, word_count INTEGER NOT NULL, read_minutes INTEGER NOT NULL,
                code_count INTEGER NOT NULL, question_count INTEGER NOT NULL,
                source_hash TEXT NOT NULL, fetched_at TEXT NOT NULL, body_json TEXT NOT NULL,
                tags_json TEXT NOT NULL DEFAULT '[]',
                UNIQUE(course, slug));

            CREATE TABLE IF NOT EXISTS questions(
                id INTEGER PRIMARY KEY, lesson_id INTEGER NOT NULL REFERENCES lessons(id) ON DELETE CASCADE,
                idx INTEGER NOT NULL, format TEXT NOT NULL, prompt TEXT NOT NULL, code TEXT,
                options_json TEXT, answer TEXT, explanation TEXT, from_source INTEGER NOT NULL,
                concept TEXT, is_gui INTEGER NOT NULL DEFAULT 0, gui_flavor TEXT, harness_code TEXT);

            CREATE TABLE IF NOT EXISTS explanations(
                id INTEGER PRIMARY KEY AUTOINCREMENT, lesson_id INTEGER NOT NULL REFERENCES lessons(id) ON DELETE CASCADE,
                title TEXT NOT NULL, kind TEXT NOT NULL, body TEXT NOT NULL);

            CREATE INDEX IF NOT EXISTS ix_lessons_chapter ON lessons(chapter_id, ord);
            CREATE INDEX IF NOT EXISTS ix_questions_lesson ON questions(lesson_id, idx);
            """);
        EnsureColumn("lessons", "tags_json", "TEXT NOT NULL DEFAULT '[]'");
        if (GetMeta("schema_version") != SchemaVersion.ToString()) SetMeta("schema_version", SchemaVersion.ToString());
    }

    /// <summary>Старая база (v3) доживает до v4 без пересборки: добавляем недостающие колонки.</summary>
    void EnsureColumn(string table, string column, string declaration)
    {
        using var probe = _conn.CreateCommand();
        probe.CommandText = $"PRAGMA table_info({table});";
        using var r = probe.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;

        using var add = _conn.CreateCommand();
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        add.ExecuteNonQuery();
    }

    public void DeleteLesson(int id)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "DELETE FROM lessons WHERE id=$id;";
        c.Parameters.AddWithValue("$id", id);
        c.ExecuteNonQuery();
    }

    /// <summary>Уроки, которых больше нет в оглавлении учебника (урок убрали с сайта).</summary>
    public List<int> LessonsNotIn(IEnumerable<int> keepIds)
    {
        var keep = new HashSet<int>(keepIds);
        var doomed = new List<int>();
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT id FROM lessons;";
        using var r = c.ExecuteReader();
        while (r.Read()) if (!keep.Contains(r.GetInt32(0))) doomed.Add(r.GetInt32(0));
        return doomed;
    }

    /// <summary>Главы без уроков (после перекладывания уроков по ступеням) — убираем.</summary>
    public void DeleteEmptyChapters()
        => Exec("DELETE FROM chapters WHERE id NOT IN (SELECT DISTINCT chapter_id FROM lessons);");

    /// <summary>Урок не менялся, но учебный план мог переложить его на другую ступень.</summary>
    public void MoveLesson(int id, int chapterId, int order)
        => Exec($"UPDATE lessons SET chapter_id={chapterId}, ord={order} WHERE id={id};");

    void Exec(string sql)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public string? GetMeta(string key)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT value FROM meta WHERE key=$k;";
        c.Parameters.AddWithValue("$k", key);
        return c.ExecuteScalar() as string;
    }

    public void SetMeta(string key, string value)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v;";
        c.Parameters.AddWithValue("$k", key);
        c.Parameters.AddWithValue("$v", value);
        c.ExecuteNonQuery();
    }

    public void UpsertChapter(Chapter ch)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "INSERT INTO chapters(id,course,number,title) VALUES($id,$c,$n,$t) ON CONFLICT(course,number) DO UPDATE SET title=$t;";
        c.Parameters.AddWithValue("$id", ch.Id);
        c.Parameters.AddWithValue("$c", ch.Course);
        c.Parameters.AddWithValue("$n", ch.Number);
        c.Parameters.AddWithValue("$t", ch.Title);
        c.ExecuteNonQuery();
    }

    public void UpsertLesson(Lesson lesson, string course = "tutorial")
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            using (var c = _conn.CreateCommand())
            {
                c.Transaction = tx;
                c.CommandText = """
                    INSERT INTO lessons(id,course,chapter_id,slug,title,url,updated_at,ord,word_count,read_minutes,
                                        code_count,question_count,source_hash,fetched_at,body_json,tags_json)
                    VALUES($id,$course,$ch,$slug,$title,$url,$upd,$ord,$words,$mins,$codes,$qs,$hash,$fetched,$body,$tags)
                    ON CONFLICT(course,slug) DO UPDATE SET
                        id=$id, chapter_id=$ch, title=$title, updated_at=$upd, ord=$ord, word_count=$words,
                        read_minutes=$mins, code_count=$codes, question_count=$qs, source_hash=$hash,
                        fetched_at=$fetched, body_json=$body, tags_json=$tags;
                    """;
                c.Parameters.AddWithValue("$id", lesson.Id);
                c.Parameters.AddWithValue("$course", course);
                c.Parameters.AddWithValue("$ch", lesson.ChapterId);
                c.Parameters.AddWithValue("$slug", lesson.Slug);
                c.Parameters.AddWithValue("$title", lesson.Title);
                c.Parameters.AddWithValue("$url", lesson.Url);
                c.Parameters.AddWithValue("$upd", lesson.UpdatedAt);
                c.Parameters.AddWithValue("$ord", lesson.Order);
                c.Parameters.AddWithValue("$words", lesson.WordCount);
                c.Parameters.AddWithValue("$mins", lesson.ReadMinutes);
                c.Parameters.AddWithValue("$codes", lesson.CodeBlocks.Count());
                c.Parameters.AddWithValue("$qs", lesson.Questions.Count);
                c.Parameters.AddWithValue("$hash", lesson.SourceHash);
                c.Parameters.AddWithValue("$fetched", DateTime.UtcNow.ToString("o"));
                c.Parameters.AddWithValue("$body", JsonSerializer.Serialize(lesson.Blocks));
                c.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(lesson.Tags));
                c.ExecuteNonQuery();
            }

            Execute($"DELETE FROM questions WHERE lesson_id={lesson.Id};");
            Execute($"DELETE FROM explanations WHERE lesson_id={lesson.Id};");

            foreach (var q in lesson.Questions)
            {
                using var c = _conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = """
                    INSERT INTO questions(lesson_id,idx,format,prompt,code,harness_code,options_json,answer,explanation,from_source,concept,is_gui,gui_flavor)
                    VALUES($l,$i,$f,$p,$code,$harness,$opt,$ans,$exp,$src,$conc,$gui,$flavor);
                    """;
                c.Parameters.AddWithValue("$l", lesson.Id);
                c.Parameters.AddWithValue("$i", q.Index);
                c.Parameters.AddWithValue("$f", q.Format.ToString());
                c.Parameters.AddWithValue("$p", q.Prompt);
                c.Parameters.AddWithValue("$code", (object?)q.Code ?? DBNull.Value);
                c.Parameters.AddWithValue("$harness", (object?)q.HarnessCode ?? DBNull.Value);
                c.Parameters.AddWithValue("$opt", JsonSerializer.Serialize(q.Options));
                c.Parameters.AddWithValue("$ans", q.CorrectAnswer);
                c.Parameters.AddWithValue("$exp", q.Explanation);
                c.Parameters.AddWithValue("$src", q.FromSource ? 1 : 0);
                c.Parameters.AddWithValue("$conc", (object?)q.ConceptTag ?? DBNull.Value);
                c.Parameters.AddWithValue("$gui", q.IsGui ? 1 : 0);
                c.Parameters.AddWithValue("$flavor", q.GuiFlavor);
                c.ExecuteNonQuery();
            }

            foreach (var e in lesson.Explanations)
            {
                using var c = _conn.CreateCommand();
                c.Transaction = tx;
                c.CommandText = "INSERT INTO explanations(lesson_id,title,kind,body) VALUES($l,$t,$k,$b);";
                c.Parameters.AddWithValue("$l", lesson.Id);
                c.Parameters.AddWithValue("$t", e.Title);
                c.Parameters.AddWithValue("$k", e.Kind.ToString());
                c.Parameters.AddWithValue("$b", e.Body);
                c.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    void Execute(string sql)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    public List<Chapter> LoadChapters()
    {
        var chapters = new Dictionary<int, Chapter>();
        using (var c = _conn.CreateCommand())
        {
            c.CommandText = "SELECT id,course,number,title FROM chapters ORDER BY number;";
            using var r = c.ExecuteReader();
            while (r.Read())
                chapters[r.GetInt32(0)] = new Chapter { Id = r.GetInt32(0), Course = r.GetString(1), Number = r.GetInt32(2), Title = r.GetString(3) };
        }

        using var cc = _conn.CreateCommand();
        cc.CommandText = """
            SELECT l.id,l.chapter_id,l.slug,l.title,l.word_count,l.read_minutes,l.code_count,l.question_count,l.ord,l.tags_json
            FROM lessons l ORDER BY l.ord;
            """;
        using (var r = cc.ExecuteReader())
        {
            while (r.Read())
            {
                var lr = new LessonRef
                {
                    Id = r.GetInt32(0),
                    ChapterId = r.GetInt32(1),
                    Slug = r.GetString(2),
                    Title = r.GetString(3),
                    WordCount = r.GetInt32(4),
                    ReadMinutes = r.GetInt32(5),
                    CodeCount = r.GetInt32(6),
                    QuestionCount = r.GetInt32(7),
                    Order = r.GetInt32(8),
                    Tags = r.IsDBNull(9) ? new() : (JsonSerializer.Deserialize<List<string>>(r.GetString(9)) ?? new()),
                };
                if (chapters.TryGetValue(lr.ChapterId, out var ch)) ch.Lessons.Add(lr);
            }
        }
        return chapters.Values.OrderBy(c => c.Number).ToList();
    }

    public Lesson? LoadLesson(int id)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT id,chapter_id,slug,title,url,updated_at,ord,word_count,read_minutes,source_hash,body_json,tags_json FROM lessons WHERE id=$id;";
        c.Parameters.AddWithValue("$id", id);
        using var r = c.ExecuteReader();
        if (!r.Read()) return null;

        var lesson = new Lesson
        {
            Id = r.GetInt32(0),
            ChapterId = r.GetInt32(1),
            Slug = r.GetString(2),
            Title = r.GetString(3),
            Url = r.GetString(4),
            UpdatedAt = r.IsDBNull(5) ? "" : r.GetString(5),
            Order = r.GetInt32(6),
            WordCount = r.GetInt32(7),
            ReadMinutes = r.GetInt32(8),
            SourceHash = r.GetString(9),
            Blocks = JsonSerializer.Deserialize<List<ContentBlock>>(r.GetString(10)) ?? new(),
            Tags = r.IsDBNull(11) ? new() : (JsonSerializer.Deserialize<List<string>>(r.GetString(11)) ?? new()),
        };

        using (var qc = _conn.CreateCommand())
        {
            qc.CommandText = "SELECT id,idx,format,prompt,code,options_json,answer,explanation,from_source,concept,is_gui,gui_flavor,harness_code FROM questions WHERE lesson_id=$l ORDER BY idx;";
            qc.Parameters.AddWithValue("$l", id);
            using var qr = qc.ExecuteReader();
            while (qr.Read())
            {
                lesson.Questions.Add(new QuizItem
                {
                    Id = qr.GetInt32(0),
                    LessonId = id,
                    Index = qr.GetInt32(1),
                    Format = Enum.TryParse<QuizFormat>(qr.GetString(2), out var f) ? f : QuizFormat.OpenText,
                    Prompt = qr.GetString(3),
                    Code = qr.IsDBNull(4) ? null : qr.GetString(4),
                    Options = JsonSerializer.Deserialize<List<string>>(qr.GetString(5)) ?? new(),
                    CorrectAnswer = qr.IsDBNull(6) ? "" : qr.GetString(6),
                    Explanation = qr.IsDBNull(7) ? "" : qr.GetString(7),
                    FromSource = qr.GetInt32(8) == 1,
                    ConceptTag = qr.IsDBNull(9) ? null : qr.GetString(9),
                    IsGui = !qr.IsDBNull(10) && qr.GetInt32(10) == 1,
                    GuiFlavor = qr.IsDBNull(11) ? "" : qr.GetString(11),
                    HarnessCode = qr.IsDBNull(12) ? null : qr.GetString(12),
                });
            }
        }

        using (var ec = _conn.CreateCommand())
        {
            ec.CommandText = "SELECT title,kind,body FROM explanations WHERE lesson_id=$l ORDER BY id;";
            ec.Parameters.AddWithValue("$l", id);
            using var er = ec.ExecuteReader();
            while (er.Read())
            {
                lesson.Explanations.Add(new Explanation
                {
                    Title = er.GetString(0),
                    Kind = Enum.TryParse<ExplanationKind>(er.GetString(1), out var k) ? k : ExplanationKind.Bridge,
                    Body = er.GetString(2),
                });
            }
        }

        return lesson;
    }

    public int CountLessons()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM lessons;";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    /// <summary>Удаляет вопрос, эталон которого не подтвердился запуском.</summary>
    public void RemoveQuestion(int questionId)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "DELETE FROM questions WHERE id=$id;";
        c.Parameters.AddWithValue("$id", questionId);
        c.ExecuteNonQuery();
    }

    public int CountQuestions()
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM questions;";
        return Convert.ToInt32(c.ExecuteScalar());
    }

    /// <summary>
    /// Закрывает базу так, чтобы файл можно было сразу прочитать/упаковать:
    /// Microsoft.Data.Sqlite пулит соединения, поэтому одного Dispose недостаточно —
    /// иначе gzip («используется другим процессом») или следующее открытие видит чужой WAL.
    /// </summary>
    public void SealAndClose()
    {
        try { Exec("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { /* уже закрыта */ }
        _conn.Dispose();
        SqliteConnection.ClearAllPools();
    }

    public void Dispose() => SealAndClose();
}
