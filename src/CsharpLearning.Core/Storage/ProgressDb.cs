using Microsoft.Data.Sqlite;

namespace CsharpLearning.Core.Storage;

public sealed record ReadingSession(int Id, int LessonId, DateTime OpenedAt, DateTime? ClosedAt, double Seconds, int Words);

public sealed record AnswerLog(int Id, int LessonId, int? QuestionId, string? Concept, string QuestionText, string Given, string Correct, bool IsCorrect, DateTime AnsweredAt, int ElapsedMs, bool IsGui);

public sealed record LessonProgress(int LessonId, double SecondsRead, int Sessions, int QuestionsAnswered, int QuestionsCorrect, DateTime? LastOpened);

/// <summary>
/// База ПРОГРЕССА ученика (progress.db). Отделена от контента намеренно:
/// обновление course.db с хостинга никогда не трогает прочитанное время и ответы.
/// </summary>
public sealed class ProgressDb : IDisposable
{
    public const int SchemaVersion = 3;

    readonly SqliteConnection _conn;
    public string FilePath { get; }

    public ProgressDb(string path)
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

            CREATE TABLE IF NOT EXISTS reading_log(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lesson_id INTEGER NOT NULL, slug TEXT, title TEXT, chapter_number INTEGER,
                opened_at TEXT NOT NULL, closed_at TEXT, seconds REAL NOT NULL DEFAULT 0, words INTEGER NOT NULL DEFAULT 0);

            CREATE TABLE IF NOT EXISTS answers(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                question_id INTEGER, lesson_id INTEGER NOT NULL, slug TEXT, concept TEXT,
                question_text TEXT, given TEXT, correct TEXT, is_correct INTEGER NOT NULL,
                answered_at TEXT NOT NULL, elapsed_ms INTEGER NOT NULL DEFAULT 0, is_gui INTEGER NOT NULL DEFAULT 0);

            CREATE TABLE IF NOT EXISTS lesson_state(
                lesson_id INTEGER PRIMARY KEY, status TEXT NOT NULL, started_at TEXT, finished_at TEXT);

            CREATE TABLE IF NOT EXISTS events(
                id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, kind TEXT NOT NULL, detail TEXT);

            CREATE INDEX IF NOT EXISTS ix_log_lesson ON reading_log(lesson_id);
            CREATE INDEX IF NOT EXISTS ix_answers_lesson ON answers(lesson_id);
            CREATE INDEX IF NOT EXISTS ix_answers_concept ON answers(concept);
            """);
        if (GetMeta("schema_version") != SchemaVersion.ToString()) SetMeta("schema_version", SchemaVersion.ToString());

        Touch("session_start", null);
    }

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

    public void Touch(string kind, string? detail)
    {
        var now = DateTime.UtcNow;
        using var c = _conn.CreateCommand();
        c.CommandText = "INSERT INTO events(at,kind,detail) VALUES($t,$k,$d);";
        c.Parameters.AddWithValue("$t", now.ToString("o"));
        c.Parameters.AddWithValue("$k", kind);
        c.Parameters.AddWithValue("$d", (object?)detail ?? DBNull.Value);
        c.ExecuteNonQuery();

        SetMeta("last_activity", now.ToString("o"));
        if (GetMeta("first_activity") is null) SetMeta("first_activity", now.ToString("o"));
    }

    // ---------------- чтение ----------------
    public int OpenLesson(int lessonId, string slug, string title, int chapterNumber)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = """
            INSERT INTO reading_log(lesson_id,slug,title,chapter_number,opened_at) VALUES($l,$s,$t,$ch,$now);
            SELECT last_insert_rowid();
            """;
        c.Parameters.AddWithValue("$l", lessonId);
        c.Parameters.AddWithValue("$s", slug);
        c.Parameters.AddWithValue("$t", title);
        c.Parameters.AddWithValue("$ch", chapterNumber);
        c.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var id = Convert.ToInt32(c.ExecuteScalar());

        using var st = _conn.CreateCommand();
        st.CommandText = """
            INSERT INTO lesson_state(lesson_id,status,started_at) VALUES($l,'reading',$now)
            ON CONFLICT(lesson_id) DO UPDATE SET status='reading';
            """;
        st.Parameters.AddWithValue("$l", lessonId);
        st.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        st.ExecuteNonQuery();

        return id;
    }

    public void CloseLesson(int sessionId, double seconds, int words, bool finished)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "UPDATE reading_log SET closed_at=$now, seconds=$s, words=$w WHERE id=$id;";
        c.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        c.Parameters.AddWithValue("$s", Math.Max(0, seconds));
        c.Parameters.AddWithValue("$w", words);
        c.Parameters.AddWithValue("$id", sessionId);
        c.ExecuteNonQuery();

        using var st = _conn.CreateCommand();
        st.CommandText = """
            INSERT INTO lesson_state(lesson_id,status,finished_at) VALUES($l,$status,$now)
            ON CONFLICT(lesson_id) DO UPDATE SET status=$status, finished_at=$now;
            """;
        st.Parameters.AddWithValue("$l", LessonIdOfSession(sessionId));
        st.Parameters.AddWithValue("$status", finished ? "done" : "reading");
        st.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        st.ExecuteNonQuery();
    }

    int LessonIdOfSession(int sessionId)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT lesson_id FROM reading_log WHERE id=$id;";
        c.Parameters.AddWithValue("$id", sessionId);
        var v = c.ExecuteScalar();
        return v is null ? 0 : Convert.ToInt32(v);
    }

    public void SetLessonStatus(int lessonId, string status)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = """
            INSERT INTO lesson_state(lesson_id,status,started_at,finished_at) VALUES($l,$s,$now,$now)
            ON CONFLICT(lesson_id) DO UPDATE SET status=$s, finished_at=$now;
            """;
        c.Parameters.AddWithValue("$l", lessonId);
        c.Parameters.AddWithValue("$s", status);
        c.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        c.ExecuteNonQuery();
    }

    public Dictionary<int, string> LoadStatuses()
    {
        var map = new Dictionary<int, string>();
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT lesson_id,status FROM lesson_state;";
        using var r = c.ExecuteReader();
        while (r.Read()) map[r.GetInt32(0)] = r.GetString(1);
        return map;
    }

    // ---------------- ответы ----------------
    public bool SaveAnswer(int lessonId, string slug, int? questionId, string? concept, string questionText,
                           string given, string correct, bool isCorrect, int elapsedMs, bool isGui)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = """
            INSERT INTO answers(question_id,lesson_id,slug,concept,question_text,given,correct,is_correct,answered_at,elapsed_ms,is_gui)
            VALUES($q,$l,$s,$c,$qt,$g,$co,$ic,$now,$ms,$gui);
            """;
        c.Parameters.AddWithValue("$q", (object?)questionId ?? DBNull.Value);
        c.Parameters.AddWithValue("$l", lessonId);
        c.Parameters.AddWithValue("$s", slug);
        c.Parameters.AddWithValue("$c", (object?)concept ?? DBNull.Value);
        c.Parameters.AddWithValue("$qt", questionText);
        c.Parameters.AddWithValue("$g", given);
        c.Parameters.AddWithValue("$co", correct);
        c.Parameters.AddWithValue("$ic", isCorrect ? 1 : 0);
        c.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        c.Parameters.AddWithValue("$ms", elapsedMs);
        c.Parameters.AddWithValue("$gui", isGui ? 1 : 0);
        c.ExecuteNonQuery();
        return isCorrect;
    }

    public List<AnswerLog> LoadAnswers(int? lessonId = null, int take = 200)
    {
        var list = new List<AnswerLog>();
        using var c = _conn.CreateCommand();
        c.CommandText = lessonId is null
            ? "SELECT id,lesson_id,question_id,concept,question_text,given,correct,is_correct,answered_at,elapsed_ms,is_gui FROM answers ORDER BY id DESC LIMIT $n;"
            : "SELECT id,lesson_id,question_id,concept,question_text,given,correct,is_correct,answered_at,elapsed_ms,is_gui FROM answers WHERE lesson_id=$l ORDER BY id DESC LIMIT $n;";
        if (lessonId is not null) c.Parameters.AddWithValue("$l", lessonId.Value);
        c.Parameters.AddWithValue("$n", take);
        using var r = c.ExecuteReader();
        while (r.Read())
            list.Add(new AnswerLog(r.GetInt32(0), r.GetInt32(1),
                r.IsDBNull(2) ? null : r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6), r.GetInt32(7) == 1,
                DateTime.Parse(r.GetString(8)), r.GetInt32(9), r.GetInt32(10) == 1));
        return list;
    }

    public bool HasAnswered(int questionId)
    {
        using var c = _conn.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM answers WHERE question_id=$q;";
        c.Parameters.AddWithValue("$q", questionId);
        return Convert.ToInt32(c.ExecuteScalar()) > 0;
    }

    public List<LessonProgress> LoadProgress()
    {
        var map = new Dictionary<int, LessonProgress>();

        using (var c = _conn.CreateCommand())
        {
            c.CommandText = """
                SELECT lesson_id, COALESCE(SUM(seconds),0), COUNT(*), MAX(closed_at)
                FROM reading_log GROUP BY lesson_id;
                """;
            using var r = c.ExecuteReader();
            while (r.Read())
                map[r.GetInt32(0)] = new LessonProgress(r.GetInt32(0), r.GetDouble(1), r.GetInt32(2), 0, 0,
                    r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3)));
        }

        using (var a = _conn.CreateCommand())
        {
            a.CommandText = "SELECT lesson_id, COUNT(*), COALESCE(SUM(is_correct),0) FROM answers GROUP BY lesson_id;";
            using var r = a.ExecuteReader();
            while (r.Read())
            {
                int lid = r.GetInt32(0);
                if (!map.TryGetValue(lid, out var p))
                    p = new LessonProgress(lid, 0, 0, 0, 0, null);
                map[lid] = p with { QuestionsAnswered = r.GetInt32(1), QuestionsCorrect = r.GetInt32(2) };
            }
        }

        return map.Values.ToList();
    }

    public void ResetProgress()
    {
        Exec("DELETE FROM reading_log; DELETE FROM answers; DELETE FROM lesson_state; DELETE FROM events;");
        SetMeta("first_activity", DateTime.UtcNow.ToString("o"));
    }

    public void Dispose() => _conn.Dispose();
}
