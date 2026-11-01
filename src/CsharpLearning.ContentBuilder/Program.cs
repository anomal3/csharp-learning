using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CsharpLearning.Core.Crawler;
using CsharpLearning.Core.Model;
using CsharpLearning.Core.Pedagogy;
using CsharpLearning.Core.Runner;
using CsharpLearning.Core.Storage;

namespace CsharpLearning.ContentBuilder;

/// <summary>
/// Собирает офлайн-контент: скачивает уроки metanit.com/sharp/tutorial, превращает их в SQLite-базу,
/// генерирует пояснения-аналогии и вопросы, а эталоны ответов получает РЕАЛЬНЫМ запуском примеров.
/// На выходе — course-vN.db.gz + manifest.json: эти два файла кладутся в /csharp/ на хостинге
/// blueboxproduction.ru, откуда приложение само обновляется.
///
/// Режимы:
///   (без флагов)   — обычная сборка: качает оглавление и уроки, обновляет изменившиеся
///   --offline      — ничего не качает: пересобирает базу из кэша content/raw (быстро, без сети)
///   --fresh        — starting with an empty database
///   --check        — только перепроверка эталонов сохранённых вопросов реальным запуском
///   --retitle      — починить подписи уроков, не трогая их содержимое
///   --limit=N      — собрать только первые N уроков (проверочный прогон)
///
/// Контент и прогресс — РАЗНЫЕ базы (course.db / progress.db), поэтому обновление уроков
/// никогда не затирает статистику прочитанного и ответов.
/// </summary>
internal static class Program
{
    static HttpClient Http = CreateClient();
    static bool Trace;
    static bool Offline;

    /// <summary>
    /// true = сначала берём страницу из кэша content/raw, в сеть идём только за тем, чего в кэше нет.
    /// --offline: только кэш. --refresh-cache: кэш + докачка недостающего.
    /// </summary>
    static bool PreferCachedPages;

    /// <summary>
    /// Готовит course.db к установщику: один файл без -wal/-shm (режим журнала DELETE), VACUUM, проверка целостности
    /// и гарантия, что в базе только контент — таблиц прогресса ученика (progress.db) в ней нет.
    /// </summary>
    static int Seal(string path)
    {
        if (!File.Exists(path)) { Console.Error.WriteLine("нет файла: " + path); return 2; }

        var tables = new List<string>();
        long lessons, questions;
        string content = "?";
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            string One(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; return Convert.ToString(c.ExecuteScalar()) ?? ""; }

            One("PRAGMA wal_checkpoint(TRUNCATE)");
            One("PRAGMA journal_mode=DELETE");
            One("VACUUM");

            string integrity = One("PRAGMA integrity_check");
            if (integrity != "ok") { Console.Error.WriteLine("integrity_check: " + integrity); return 2; }

            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                using var r = c.ExecuteReader();
                while (r.Read()) tables.Add(r.GetString(0));
            }
            lessons = long.Parse(One("SELECT COUNT(*) FROM lessons"));
            questions = long.Parse(One("SELECT COUNT(*) FROM questions"));
            content = One("SELECT value FROM meta WHERE key='content_version'");
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var side in new[] { path + "-wal", path + "-shm", path + "-journal" })
            if (File.Exists(side)) File.Delete(side);

        var forbidden = tables.Where(t => t is "reading_log" or "answers" or "lesson_state" or "events").ToList();
        if (forbidden.Count > 0) { Console.Error.WriteLine("в базе контента найден прогресс ученика: " + string.Join(", ", forbidden)); return 2; }
        if (lessons == 0 || questions == 0) { Console.Error.WriteLine("база пуста"); return 2; }

        Console.WriteLine($"course.db готова: версия контента {content}, уроков {lessons}, вопросов {questions}, таблицы: {string.Join(", ", tables)}");
        return 0;
    }

    static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 6,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        // Сайт отдаёт 403 «голому» запросу: без полного набора заголовков контент не получить.
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9");
        return c;
    }

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        var sealArg = args.FirstOrDefault(a => a.StartsWith("--seal="));
        if (sealArg is not null) return Seal(sealArg["--seal=".Length..]);

        string root = args.FirstOrDefault(a => !a.StartsWith("--")) ?? FindProjectRoot();
        string dataDir = Path.Combine(root, "content");
        string outDir = Path.Combine(root, "publish");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(outDir);

        Trace = args.Contains("--trace");
        Offline = args.Contains("--offline");
        bool fresh = args.Contains("--fresh");
        bool checkMode = args.Contains("--check");
        bool retitle = args.Contains("--retitle");

        string dbPath = Path.Combine(dataDir, "course.db");
        string cacheDir = Path.Combine(dataDir, "raw");
        Directory.CreateDirectory(cacheDir);

        // --offline берём всё из content/raw. --refresh-cache — то же самое, но страницы,
        // которых в кэше нет, докачиваем: так можно «дособрать» курс, не перекачивая 195 страниц.
        bool refreshCache = args.Contains("--refresh-cache");
        PreferCachedPages = Offline || refreshCache;

        if (fresh && File.Exists(dbPath)) File.Delete(dbPath);

        int limit = int.MaxValue;
        var limArg = args.FirstOrDefault(a => a.StartsWith("--limit="));
        if (limArg is not null && int.TryParse(limArg["--limit=".Length..], out var lim)) limit = lim;

        var sw = Stopwatch.StartNew();
        Console.WriteLine("=== Сбор учебного контента (офлайн-база для приложения) ===");
        Console.WriteLine($"база: {dbPath}   режим: {(Offline ? "OFFLINE (кэш content/raw)" : checkMode ? "только проверка эталонов" : retitle ? "починка названий" : "сеть + кэш")}");

        using var db = new ContentDb(dbPath);

        if (checkMode)
        {
            int rc = await CheckExamplesAsync(db);
            SealVersion(db, outDir, sw, lessonsChanged: 0);
            Console.WriteLine($"проверка эталонов: {(rc == 0 ? "ВСЕ СОВПАЛИ" : "ЕСТЬ РАСХОЖДЕНИЯ")} за {sw.Elapsed.TotalSeconds:F1} с");
            return rc;
        }

        // 1. Оглавление учебникa: slug → название именно то, что видно в списке глав
        List<(int number, string title, List<MetanitParser.TocLesson> lessons)> toc;
        List<string> slugs;

        if (Offline)
        {
            (toc, slugs) = OfflineToc(cacheDir);
        }
        else
        {
            var indexHtml = await GetString(MetanitParser.BaseUrl);
            File.WriteAllText(Path.Combine(cacheDir, "_index.html"), indexHtml);
            MetanitParser.BuildToc(indexHtml, MetanitParser.BaseUrl, out var s, out toc);
            slugs = s;
        }

        Console.WriteLine($"глав: {toc.Count}, уроков в оглавлении: {slugs.Count}");

        if (retitle)
        {
            int fixedCount = RetitleLessons(db, toc);
            Console.WriteLine($"названий исправлено: {fixedCount}");
            SealVersion(db, outDir, sw, lessonsChanged: fixedCount);
            return 0;
        }

        // Порядок и ступени задаёт учебный план (Curriculum): от простого к сложному,
        // а между уроками metanit вставлены авторские вводные уроки.
        var tocBySlug = toc.SelectMany(t => t.lessons).GroupBy(l => l.Slug).ToDictionary(g => g.Key, g => g.First());
        var syllabus = new List<(Curriculum.Stage stage, string slug)>();
        var planned = new HashSet<string>();
        foreach (var st in Curriculum.Stages)
            foreach (var slug in st.Slugs)
            {
                if (!planned.Add(slug)) { Console.WriteLine($"  ! урок {slug} в плане дважды"); continue; }
                if (Curriculum.IsAuthored(slug) || tocBySlug.ContainsKey(slug)) syllabus.Add((st, slug));
                else Console.WriteLine($"  ! урок {slug} есть в плане, но нет в оглавлении");
            }
        var unplanned = tocBySlug.Keys.Where(sl => !planned.Contains(sl)).ToList();
        foreach (var sl in unplanned) syllabus.Add((Curriculum.ExtraStage, sl));
        if (unplanned.Count > 0) Console.WriteLine($"  ! уроков вне плана (в «Дополнительные»): {unplanned.Count}: {string.Join(", ", unplanned)}");

        var orderOf = new Dictionary<string, int>();
        foreach (var (_, slug) in syllabus) orderOf[slug] = orderOf.Count + 1;

        foreach (var st in syllabus.Select(x => x.stage).Distinct())
            db.UpsertChapter(new Chapter { Id = st.Number, Number = st.Number, Title = st.FullTitle, Course = "tutorial" });

        var workDir = Path.Combine(dataDir, "build");
        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        // Один и тот же пример (особенно GUI-рецепт по концепции) встречается в десятках уроков:
        // компилируем и запускаем его один раз, дальше — из кэша.
        var verifiedCache = new ExampleCache(workDir);

        var queue = new List<(int chapterId, MetanitParser.TocLesson lesson)>();
        foreach (var (stage, slug) in syllabus)
        {
            var title = Curriculum.IsAuthored(slug) ? AuthoredLessons.All.First(a => a.Slug == slug).Title : tocBySlug[slug].Title;
            queue.Add((stage.Number, new MetanitParser.TocLesson(slug, title)));
        }
        int tocQueueCount = queue.Count(q => !Curriculum.IsAuthored(q.lesson.Slug));
        if (queue.Count > limit) queue = queue.Take(limit).ToList();
        Console.WriteLine($"к обработке: {queue.Count} уроков ({AuthoredLessons.All.Count} авторских)");

        int done = 0, skipped = 0, failed = 0, rejected = 0;
        const int Batch = 6;

        for (int b = 0; b < queue.Count; b += Batch)
        {
            var batch = queue.GetRange(b, Math.Min(Batch, queue.Count - b));
            var pages = await Task.WhenAll(batch.Select(x => Curriculum.IsAuthored(x.lesson.Slug)
                ? Task.FromResult<string?>("")
                : DownloadLesson(x.lesson.Slug, cacheDir)));

            // 1) собираем кандидатов по уроку, 2) проверяем их запуском параллельно, 3) сохраняем урок
            var plans = new List<LessonPlan>();

            for (int k = 0; k < batch.Count; k++)
            {
                var tocLesson = batch[k].lesson;
                int chapterId = batch[k].chapterId;
                string? html = pages[k];
                if (html is null) { failed++; Console.WriteLine($"  ✗ {tocLesson.Slug}: недоступен"); continue; }

                string url = MetanitParser.BaseUrl + tocLesson.Slug;
                LessonParseResult parsed;
                string hash;
                if (Curriculum.IsAuthored(tocLesson.Slug))
                {
                    var au = AuthoredLessons.All.First(a => a.Slug == tocLesson.Slug);
                    var plain = string.Join("\n", au.Blocks.Where(x => x.Kind != BlockKind.Code).Select(x => x.Text));
                    parsed = new LessonParseResult(au.Title, "", au.Blocks, plain, au.Blocks.Count(x => x.Kind == BlockKind.Code), null);
                    url = "";
                    hash = MetanitParser.Hash(JsonSerializer.Serialize(au.Blocks));
                }
                else
                {
                    parsed = MetanitParser.ParseLesson(html, url, tocLesson.Title);
                    hash = MetanitParser.Hash(html);
                }

                // Название из оглавления надёжнее, чем то, что стоит внутри страницы
                string title = PickTitle(tocLesson.Title, parsed.Title, tocLesson.Slug);
                int lessonId = LessonIdOf(tocLesson.Slug);

                if (!fresh && db.GetMeta("hash_" + tocLesson.Slug) == hash) { db.MoveLesson(lessonId, chapterId, orderOf[tocLesson.Slug]); skipped++; continue; }

                var lesson = new Lesson
                {
                    Id = lessonId,
                    ChapterId = chapterId,
                    Slug = tocLesson.Slug,
                    Title = title,
                    Url = url,
                    UpdatedAt = parsed.UpdatedAt,
                    Order = orderOf[tocLesson.Slug],
                    WordCount = Words(parsed.PlainText),
                    ReadMinutes = Math.Max(1, (int)Math.Round(Words(parsed.PlainText) / 200.0)),
                    SourceHash = hash,
                    Blocks = parsed.Blocks,
                    Tags = TagMapper.Of(title, parsed.PlainText),
                };

                // Пояснения «не сухая теория»: аналогии с животными/бытом + перевод в код + ловушки
                var analogies = ExplanationEngine.Match(title, parsed.PlainText);
                foreach (var a in analogies)
                {
                    lesson.Explanations.Add(new Explanation { Title = a.Concept + " — на примере", Kind = ExplanationKind.Analogy, Body = a.AnimalPicture });
                    lesson.Explanations.Add(new Explanation { Title = a.Concept + " — в быту", Kind = ExplanationKind.Everyday, Body = a.Everyday });
                    lesson.Explanations.Add(new Explanation { Title = a.Concept + " — как выглядит в коде", Kind = ExplanationKind.Bridge, Body = a.Bridge });
                    foreach (var trap in a.Trap)
                        lesson.Explanations.Add(new Explanation { Title = "Частая ошибка", Kind = ExplanationKind.Mistake, Body = trap });
                }

                var sourceQuiz = new List<QuizItem>();

                // Вопросы «самопроверки» прямо с metanit (с их же ответами).
                // Страница вопросов кэшируется рядом с уроком: офлайн-пересборка воспроизводит
                // базу без сети, а повторная сборка не дёргает сайт за уже скачанным.
                if (parsed.QuestionsUrl is not null)
                {
                    var cacheQ = Path.Combine(cacheDir, tocLesson.Slug + ".questions.html");
                    string? qHtml = File.Exists(cacheQ) ? await File.ReadAllTextAsync(cacheQ) : null;

                    if (qHtml is null && !Offline)
                    {
                        qHtml = await GetStringSafe(parsed.QuestionsUrl);
                        if (qHtml is not null)
                        {
                            try { await File.WriteAllTextAsync(cacheQ, qHtml); } catch { /* кэш — приятное дополнение */ }
                        }
                    }

                    if (qHtml is not null) sourceQuiz.AddRange(MetanitParser.ParseQuestions(qHtml, lessonId));
                }

                var concepts = analogies.Select(a => a.Concept).ToList();
                if (concepts.Count == 0) concepts.Add(GuessConcept(title));

                var candidates = new List<(GeneratedExample example, string concept, bool gui)>();

                // Примеры самого урока → вопросы «что выведет»: код берём из урока, эталон — запуск
                foreach (var (ge, concept) in LessonExamples(parsed, concepts))
                    candidates.Add((ge, concept, false));

                foreach (var concept in concepts.Take(3))
                {
                    foreach (var ge in ExampleGenerator.For(concept).Take(2))
                        candidates.Add((ge, concept, false));

                    candidates.Add((ExampleGenerator.WinFormsExample(concept), concept, true));
                    candidates.Add((ExampleGenerator.WpfExample(concept), concept, true));
                }

                plans.Add(new LessonPlan(lesson, sourceQuiz, candidates));
            }

            // Параллельная верификация: компиляция + запуск в песочнице, потолок — 4 процесса сразу
            var verifyResults = new List<List<QuizItem>>();
            foreach (var plan in plans)
            {
                var items = new List<QuizItem>(plan.sourceQuiz);
                var verified = await Task.WhenAll(plan.candidates.Select(async c =>
                {
                    var ok = await verifiedCache.VerifyAsync(c.example);
                    if (ok is null) return null;
                    var item = ToQuiz(ok, plan.lesson.Id, c.concept);
                    if (c.gui) { item.IsGui = true; item.GuiFlavor = c.example.IsGui ? GuessFlavor(c.example.Code) : ""; }
                    return item;
                }));

                foreach (var item in verified)
                {
                    if (item is null) { rejected++; continue; }
                    items.Add(item);
                }

                for (int qi = 0; qi < items.Count; qi++) { items[qi].Index = qi; items[qi].LessonId = plan.lesson.Id; }
                verifyResults.Add(items);
            }

            for (int i = 0; i < plans.Count; i++)
            {
                plans[i].lesson.Questions = verifyResults[i];
                db.UpsertLesson(plans[i].lesson);
                db.SetMeta("hash_" + plans[i].lesson.Slug, plans[i].lesson.SourceHash);
                done++;
            }

            Console.WriteLine($"  {Math.Min(b + Batch, queue.Count)}/{queue.Count}: новых {done}, без изменений {skipped}, ошибок {failed}, примеров проверено {verifiedCache.VerifiedCount} (уникальных {verifiedCache.UniqueCount}), отклонено {rejected}");
            await Task.Delay(120);
        }

        // Уроки, которых больше нет в оглавлении (учебник их переиздал/убрал) — вычищаем
        // Вычищать можно только полную сборку: при --limit=N в queue всего N уроков,
// и «лишними» выглядели бы все остальные сто восемьдесят.
        bool fullBuild = limit == int.MaxValue && queue.Count >= tocQueueCount;
        if (fullBuild)
        {
            var doomed = db.LessonsNotIn(queue.Select(q => LessonIdOf(q.lesson.Slug)));
            if (doomed.Count > 0)
            {
                foreach (var id in doomed) db.DeleteLesson(id);
                Console.WriteLine($"убрано уроков, которых нет в оглавлении: {doomed.Count}");
            }
        }
        else
        {
            Console.WriteLine("прореживание пропущено: сборка частичная (--limit)");
        }

        db.DeleteEmptyChapters();

        // Итоговая проверка всех эталонов уже сохранённой базы
        Console.WriteLine();
        Console.WriteLine("--- контроль эталонов сохранённых вопросов ---");
        int checkRc = await CheckExamplesAsync(db);

        return SealVersion(db, outDir, sw, lessonsChanged: done);
    }

    /// <summary>Финальный счётчик версии + пакет для хостинга.</summary>
    static int SealVersion(ContentDb db, string outDir, Stopwatch sw, int lessonsChanged)
    {
        int version = (db.GetMeta("content_version") is null ? 0 : int.Parse(db.GetMeta("content_version")!)) + 1;
        int lessons = db.CountLessons();
        int questions = db.CountQuestions();
        db.SetMeta("content_version", version.ToString());
        db.SetMeta("built_at", DateTime.UtcNow.ToString("o"));
        db.SetMeta("source", MetanitParser.BaseUrl);
        var chapters = db.LoadChapters();
        db.SetMeta("chapters", chapters.Count.ToString());
        db.SetMeta("tags_json", JsonSerializer.Serialize(TagMapper.All));
        db.Dispose();

        var packPath = Path.Combine(outDir, "course-v" + version + ".db.gz");
        GzipFile(Path.Combine(Path.GetDirectoryName(db.FilePath)!, "course.db"), packPath);

        // ВАЖНО: внутри инициализатора словаря нельзя писать ["ключ"] = new[] { … } —
        // компилятор не может разобрать, к чему относятся фигурные скобки. Поэтому массивы
        // манифеста собираются отдельными переменными.
        var fileEntry = new Dictionary<string, object>
        {
            ["name"] = Path.GetFileName(packPath),
            ["sha256"] = Sha256File(packPath),
            ["size"] = new FileInfo(packPath).Length,
        };

        var manifest = new Dictionary<string, object>
        {
            ["schema"] = ContentDb.SchemaVersion,
            ["contentVersion"] = version,
            ["builtAt"] = DateTime.UtcNow.ToString("o"),
            ["source"] = MetanitParser.BaseUrl,
            ["lessons"] = lessons,
            ["questions"] = questions,
            ["files"] = new List<Dictionary<string, object>> { fileEntry },
            ["nextCourses"] = new List<string>
            {
                "https://metanit.com/sharp/dotnet/",
                "https://metanit.com/sharp/windowsforms/",
                "https://metanit.com/sharp/wpf/",
            },
        };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine();
        Console.WriteLine($"=== Готово за {sw.Elapsed.TotalSeconds:F1} с ===");
        Console.WriteLine($"уроков в базе: {lessons}, обновлено сейчас: {lessonsChanged}");
        Console.WriteLine($"вопросов: {questions}");
        Console.WriteLine($"версия контента: {version}");
        Console.WriteLine($"пакет: {packPath} ({new FileInfo(packPath).Length / 1024} КБ)");
        Console.WriteLine($"манифест: {Path.Combine(outDir, "manifest.json")}");
        Console.WriteLine("→ эти два файла кладутся в папку /csharp/ на blueboxproduction.ru");
        return 0;
    }

    /// <summary>План урока: что сохранить и какие примеры надо проверить запуском.</summary>
    sealed record LessonPlan(Lesson lesson, List<QuizItem> sourceQuiz, List<(GeneratedExample example, string concept, bool gui)> candidates);

    /// <summary>
    /// Кэш «проверено запуском»: один и тот же код (особенно GUI-рецепт по концепции)
    /// встречается в десятках уроков — компилируем и запускаем его один раз.
    /// </summary>
    sealed class ExampleCache
    {
        readonly Dictionary<string, GeneratedExample?> _cache = new();
        readonly SemaphoreSlim _gate = new(4);
        readonly string _workDir;

        public ExampleCache(string workDir) => _workDir = workDir;

        public int VerifiedCount { get; private set; }
        public int UniqueCount => _cache.Count;

        public async Task<GeneratedExample?> VerifyAsync(GeneratedExample ge)
        {
            string code = ge.HarnessCode ?? ge.Code;
            if (string.IsNullOrWhiteSpace(code))
                return ge.Format == QuizFormat.MultipleChoice ? ge : null;

            // GUI-пример: консольный каркас даёт эталон, но само окно ученик увидит живьём —
            // поэтому дополнительно проверяем, что оконный код хотя бы компилируется.
            if (ge.IsGui && ge.HarnessCode is not null)
            {
                string guiKey = "gui:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ge.Code)));
                bool guiOk;
                lock (_cache) guiOk = _cache.TryGetValue(guiKey, out var hit) ? hit is not null : false;

                if (!guiOk)
                {
                    await _gate.WaitAsync();
                    try
                    {
                        var compiled = await Task.Run(() => IlWorkbench.Compile(ge.Code, _workDir, "gui_" + Math.Abs(StableHash(ge.Code))));
                        guiOk = compiled.Success;
                        if (!compiled.Success && Trace)
                            Console.WriteLine($"      [gui/{ge.Concept}] оконный код не компилируется: {compiled.Diagnostics.Replace("\n", " | ")}");
                        lock (_cache) _cache[guiKey] = guiOk ? ge : null;
                    }
                    finally { _gate.Release(); }
                }

                if (!guiOk) return null; // вопрос с uncompilable окном в базу не попадает
            }

            string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(code)));

            lock (_cache)
                if (_cache.TryGetValue(key, out var cached)) { VerifiedCount++; return cached; }

            await _gate.WaitAsync();
            try
            {
                lock (_cache)
                    if (_cache.TryGetValue(key, out var already)) { VerifiedCount++; return already; }

                var verified = await VerifyExample(ge, _workDir, "cache");
                lock (_cache) _cache[key] = verified;
                if (verified is not null) VerifiedCount++;
                return verified;
            }
            finally { _gate.Release(); }
        }
    }

    static string GuessFlavor(string code) =>
        code.Contains("System.Windows.Forms") || code.Contains("Application.Run") ? "WinForms" : "WPF";

    /// <summary>
    /// Офлайн-оглавление: оглавление берём из кэша content/raw/_index.html, а если его нет —
    /// собираем список из самих закэшированных страниц. Так пересборка работает без сети.
    /// </summary>
    static (List<(int number, string title, List<MetanitParser.TocLesson> lessons)>, List<string>) OfflineToc(string cacheDir)
    {
        var toc = new List<(int number, string title, List<MetanitParser.TocLesson> lessons)>();
        var slugs = new List<string>();

        var indexPath = Path.Combine(cacheDir, "_index.html");
        if (File.Exists(indexPath))
        {
            MetanitParser.BuildToc(File.ReadAllText(indexPath), MetanitParser.BaseUrl, out var s, out toc);
            slugs = s;
            if (toc.Count > 0) return (toc, slugs);
        }

        var byChapter = new Dictionary<int, List<MetanitParser.TocLesson>>();
        foreach (var file in Directory.GetFiles(cacheDir, "*.html"))
        {
            var slug = Path.GetFileName(file)[..^".html".Length];
            var parts = slug.Replace(".php", "").Split('.');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var ch) || !int.TryParse(parts[1], out _)) continue;

            var html = File.ReadAllText(file);
            string title = MetanitParser.TitleFromHead(html);
            if (string.IsNullOrWhiteSpace(title)) title = slug;
            if (!byChapter.TryGetValue(ch, out var list)) byChapter[ch] = list = new();
            list.Add(new MetanitParser.TocLesson(slug, title));
        }

        foreach (var ch in byChapter.Keys.OrderBy(c => c))
        {
            var lessons = byChapter[ch].OrderBy(l => LessonIdOf(l.Slug)).ToList();
            toc.Add((ch, "Глава " + ch, lessons));
            slugs.AddRange(lessons.Select(l => l.Slug));
        }
        return (toc, slugs);
    }

    /// <summary>
    /// Починка подписей уроков в уже собранной базе: берём название из оглавления и &lt;title&gt; страницы,
    /// содержимое урока не трогаем.
    /// </summary>
    static int RetitleLessons(ContentDb db, List<(int number, string title, List<MetanitParser.TocLesson> lessons)> toc)
    {
        var wanted = toc.SelectMany(t => t.lessons).ToDictionary(l => l.Slug, l => l.Title);
        int fixedCount = 0;

        foreach (var ch in db.LoadChapters())
        {
            foreach (var lref in ch.Lessons)
            {
                if (!wanted.TryGetValue(lref.Slug, out var tocTitle)) continue;
                var lesson = db.LoadLesson(lref.Id);
                if (lesson is null) continue;

                string htmlPath = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "raw", lref.Slug + ".html");
                string fromPage = File.Exists(htmlPath) ? MetanitParser.TitleFromHead(File.ReadAllText(htmlPath)) : "";
                string title = PickTitle(tocTitle, fromPage, lref.Slug);

                if (title == lesson.Title) continue;

                var rebuilt = new Lesson
                {
                    Id = lesson.Id,
                    ChapterId = lesson.ChapterId,
                    Slug = lesson.Slug,
                    Title = title,
                    Url = lesson.Url,
                    UpdatedAt = lesson.UpdatedAt,
                    Order = lesson.Order,
                    WordCount = lesson.WordCount,
                    ReadMinutes = lesson.ReadMinutes,
                    SourceHash = lesson.SourceHash,
                    Blocks = lesson.Blocks,
                    Tags = lesson.Tags.Count > 0 ? lesson.Tags : TagMapper.Of(title, string.Join(" ", lesson.Blocks.Where(b => b.Kind != BlockKind.Code).Select(b => b.Text))),
                    Explanations = lesson.Explanations,
                    Questions = lesson.Questions,
                };
                db.UpsertLesson(rebuilt);
                fixedCount++;
                Console.WriteLine($"  {lref.Slug}: «{lref.Title}» → «{title}»");
            }
        }
        return fixedCount;
    }

    /// <summary>
    /// Название урока. Оглавление учебника надёжнее подписи внутри страницы, но в оглавлении
    /// у ссылки на первую страницу главы стоит название САМОЙ ГЛАВЫ («Глава 2. Основы
    /// программирования на C#») — урок так подписывать нельзя, иначе он выглядит как раздел.
    /// Для таких уроков берём &lt;title&gt; страницы.
    /// </summary>
    static string PickTitle(string fromToc, string fromPage, string slug)
    {
        static bool Bad(string s) => string.IsNullOrWhiteSpace(s) || s.EndsWith(".php") || s.Length < 3;
        static bool ChapterLike(string s) =>
            System.Text.RegularExpressions.Regex.IsMatch(s, @"^\s*(глава|раздел|приложение)\s*\d", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (!Bad(fromToc) && !ChapterLike(fromToc)) return fromToc;
        if (!Bad(fromPage)) return fromPage;
        if (!Bad(fromToc)) return fromToc;
        return slug;
    }

    /// <summary>
    /// Примеры самого урока как вопросы «что выведет»: код из урока, эталон — реальный запуск.
    /// Берутся только примеры, похожие на самостоятельный код (с Console/циклом/объявлением),
    /// чтобы не превращать фрагмент-подсказку в вопрос.
    /// </summary>
    static IEnumerable<(GeneratedExample, string concept)> LessonExamples(LessonParseResult parsed, List<string> concepts)
    {
        var concept = concepts.FirstOrDefault() ?? GuessConcept(parsed.Title);
        int taken = 0;

        foreach (var block in parsed.Blocks)
        {
            if (block.Kind != BlockKind.Code || block.Code is null) continue;
            var code = block.Code.Trim();
            if (!LooksRunnable(code)) continue;
            if (code.Length > 1800) continue;

            yield return (new GeneratedExample
            {
                Concept = concept,
                Format = QuizFormat.PredictOutput,
                Prompt = "Пример из этого урока. Что выведет программа? (эталон получен реальным запуском)",
                Code = code,
                Explanation = "Пример взят из текста урока «" + parsed.Title + "». Ответ совпадает с выводом этого кода.",
                NeedsVerification = true,
            }, concept);

            if (++taken >= 2) yield break;
        }
    }

    static bool LooksRunnable(string code)
    {
        if (code.Length < 12) return false;
        if (code.Count(c => c == '\n') < 1) return false;
        bool callsSomething = code.Contains("Console.") || code.Contains("WriteLine");
        bool declares = code.Contains("class ") || code.Contains("struct ") || code.Contains("int ") ||
                        code.Contains("string ") || code.Contains("var ") || code.Contains("for (") ||
                        code.Contains("while (") || code.Contains("if (") || code.Contains("static ");
        bool hasMain = code.Contains("static void Main") || code.Contains("static async Task Main");
        bool truncated = code.TrimEnd().EndsWith("...") || code.Contains("…");
        return (callsSomething || hasMain) && declares && !truncated && !NeedsOutsideWorld(code);
    }

    /// <summary>
    /// Пример, которому нужен внешний мир, нельзя проверить запуском: у нас нет чужих
    /// NuGet-пакетов, нет указанных в тексте файлов и нет другой сборки. Такие фрагменты
    /// учебника — иллюстрация, а не программа: отсей их до компиляции.
    /// </summary>
    /// <summary>Эталон для программы, которая ничего не печатает.</summary>
    static readonly string EmptyOutput = "(ничего не выведено)";

    /// <summary>Пример обращается к диску: такого диска у проверяющего нет.</summary>
    static readonly string[] FileApis = new[]
    {
        "File.Open", "File.ReadAllText", "File.ReadAllLines", "File.ReadAllBytes",
        "File.WriteAllText", "File.WriteAllLines", "File.AppendAllText", "File.Create",
        "File.Copy", "File.Move", "File.Delete", "File.Exists", "File.ReadLines",
        "new StreamWriter(", "new StreamReader(", "new FileStream(",
        "XmlDocument()", "XDocument.Load(", "XmlSerializer(", "new XDocument(",
        "ZipFile.CreateFromDirectory", "ZipFile.OpenRead", "Directory.GetFiles",
    };

    /// <summary>Расширения файлов, которые примеры учебника читают «из текущей папки».</summary>
    static readonly string[] DataExtensions = new[]
        { ".txt", ".xml", ".json", ".csv", ".dat", ".bin", ".zip", ".dll" };

    /// <summary>
    /// Вывод этих API differs от запуска к запуску и от машины к машине: эталон, снятый
    /// на нашем ПК, оказался бы неверным на ПК ученика. Отсеиваем до компиляции.
    /// </summary>
    static readonly string[] NondeterministicApis = new[]
    {
        "Process.GetProcesses", "Process.GetCurrentProcess", "DriveInfo",
        "GetLogicalDrives", "GetHostName", "Guid.NewGuid", "DateTime.Now", "DateTime.Today",
        "Random(", "AsParallel", "Parallel.For", "Environment.GetEnvironmentVariable",
        "Environment.OSVersion", "Environment.TickCount",
    };

    static bool NeedsOutsideWorld(string code)
    {
        foreach (var pkg in new[]
        {
            "Newtonsoft", "IronPython", "Serilog", "Npgsql", "MySql", "EntityFramework",
            "Microsoft.EntityFrameworkCore", "CsvHelper", "DocumentFormat.OpenXml", "MailKit",
            "System.Management", "Microsoft.CSharp.Interop", "System.Windows.Interop",
        })
            if (code.Contains(pkg)) return true;

        // жёсткие пути из учебника (c:\app\..., D:\test) и загрузка чужой сборки
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"[""'][A-Za-z]:[\\/]")) return true;
        if (code.Contains("LoadFromAssemblyPath") || code.Contains("Assembly.LoadFrom")) return true;

        // пример читает/пишет файл по имени из текущей папки — такой файл существует только
        // в голове автора учебника. Кавычки внутри verbatim-регулярки писать нельзя:
        // [""] компилятор читает как «кавычка + конец строки» и ломает весь метод.
        bool touchesFile = false;
        foreach (var api in FileApis)
            if (code.Contains(api)) { touchesFile = true; break; }

        if (touchesFile)
        {
            foreach (var ext in DataExtensions)
                if (code.Contains(ext)) return true;
        }

        // обещание «запусти и введи с клавиатуры» в примерах учебника встречается постоянно
        if (code.Contains("Console.ReadLine") || code.Contains("Console.ReadKey")) return true;

        // вывод этих API меняется от запуска к запуску и от машины к машине: эталон,
        // снятый на нашем ПК, был бы неверен на ПК ученика
        foreach (var api in NondeterministicApis)
            if (code.Contains(api)) return true;

        // gui-каркас (у него свой путь проверки) и явно бесконечные примеры
        if (code.Contains("Application.Run") || code.Contains("while (true)") || code.Contains("for (; ; )")) return true;

        return false;
    }

    static QuizItem ToQuiz(GeneratedExample ge, int lessonId, string concept) => new()
    {
        LessonId = lessonId,
        Format = ge.Format,
        Prompt = ge.Prompt,
        Code = ge.Code,
        HarnessCode = ge.HarnessCode,
        Options = ge.Options,
        CorrectAnswer = ge.CorrectAnswer,
        Explanation = ge.Explanation,
        FromSource = false,
        ConceptTag = concept,
        IsGui = ge.IsGui,
    };

    /// <summary>
    /// Перепроверяет сохранённые эталоны: заново запускает код вопроса и сверяет вывод.
    /// Если хотя бы один эталон не сошёлся — вопрос исключается, а не «чинится» на глаз.
    /// </summary>
    static async Task<int> CheckExamplesAsync(ContentDb db)
    {
        var workDir = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "check");
        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        var stale = new List<(int questionId, string reason)>();
        var checkedCount = 0;

        var targets = new List<QuizItem>();
        foreach (var ch in db.LoadChapters())
        {
            foreach (var lref in ch.Lessons)
            {
                var lesson = db.LoadLesson(lref.Id);
                if (lesson is null) continue;

                foreach (var q in lesson.Questions)
                {
                    if (q.Format != QuizFormat.PredictOutput) continue;

                    // Для GUI-вопросов эталон проверяется консольным каркасом (harness)
                    string? code = q.HarnessCode ?? (q.IsGui ? null : q.Code);
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    targets.Add(q);
                }
            }
        }

        var gate = new SemaphoreSlim(6);
        var results = new List<(int id, string? reason)>();

        await Task.WhenAll(targets.Select(async q =>
        {
            string? code = q.HarnessCode ?? (q.IsGui ? null : q.Code)!;
            await gate.WaitAsync();
            try
            {
                var compiled = IlWorkbench.Compile(code, workDir, "chk_" + q.Id);
                if (!compiled.Success)
                {
                    results.Add((q.Id, "не компилируется: " + compiled.Diagnostics.Split('\n')[0]));
                    return;
                }

                var run = SandboxRunner.Run(compiled.AssemblyPath!, 12000, workDir: workDir);
                if (run.TimedOut) { results.Add((q.Id, "не завершилась (бесконечный цикл?)")); return; }

                var actual = Normalize(run.Output);
                var expected = Normalize(q.CorrectAnswer);
                // Правило эталона должно быть ОДИНАКОВЫМ при сборке и при контроле: иначе
                // вопросы «ничего не выведено» отсеивались бы как битые на каждом прогоне.
                if (actual.Length == 0) actual = EmptyOutput;

                if (actual == expected) { results.Add((q.Id, null)); return; }

                // Некоторые примеры учебника печатают в нестабильном порядке (GroupBy без
                // OrderBy, несколько async-записей в консоль). Эталон, снятый реально запуском,
                // не «битый» — просто вывод не deterministic: перепроверяем вторым запуском,
                // и если он совпал с эталоном — оставляем вопрос, а не выбрасываем.
                var again = Normalize(SandboxRunner.Run(compiled.AssemblyPath!, 12000, workDir: workDir).Output);
                if (again.Length == 0) again = EmptyOutput;
                if (again == expected) { results.Add((q.Id, null)); return; }

                results.Add((q.Id, $"эталон «{Short(expected)}» ≠ запуск «{Short(actual)}»"));
            }
            finally { gate.Release(); }
        }));

        checkedCount = results.Count;
        stale.AddRange(results.Where(r => r.reason is not null).Select(r => (r.id, r.reason!)));

        foreach (var (id, reason) in stale)
        {
            if (Trace) Console.WriteLine($"  ✗ вопрос {id}: {reason}");
            db.RemoveQuestion(id);
        }

        Console.WriteLine($"  проверено вопросов с кодом: {checkedCount}, отклонено: {stale.Count}");
        return stale.Count == 0 ? 0 : 2;

        static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();
        static string Short(string s) => s.Length <= 60 ? s : s[..57] + "…";
    }

    // ---------------- helpers ----------------

    static async Task<string?> DownloadLesson(string slug, string cacheDir)
    {
        var cacheFile = Path.Combine(cacheDir, slug + ".html");

        // Кэш — первый источник: сеть нужна только для недостающих страниц.
        if (PreferCachedPages && File.Exists(cacheFile)) return await File.ReadAllTextAsync(cacheFile);
        if (Offline) return null;

        try
        {
            var raw = await Http.GetByteArrayAsync(MetanitParser.BaseUrl + slug);
            var html = MetanitParser.Decode(raw);
            await File.WriteAllTextAsync(cacheFile, html);
            return html;
        }
        catch (Exception ex)
        {
            if (File.Exists(cacheFile)) return await File.ReadAllTextAsync(cacheFile);
            if (Trace) Console.WriteLine($"    ! {slug}: {ex.Message}");
            return null;
        }
    }

    static async Task<string> GetString(string url)
    {
        var raw = await Http.GetByteArrayAsync(url);
        return MetanitParser.Decode(raw);
    }

    static async Task<string?> GetStringSafe(string url)
    {
        if (Offline) return null;
        try { return await GetString(url); } catch { return null; }
    }

    /// <summary>Эталонный ответ «что выведет» = реальный запуск примера, а не догадка.</summary>
    static async Task<GeneratedExample?> VerifyExample(GeneratedExample ge, string workDir, string slug)
    {
        string code = ge.HarnessCode ?? ge.Code;
        if (string.IsNullOrWhiteSpace(code)) return ge.Format == QuizFormat.MultipleChoice ? ge : null;

        string name = "ex_" + Math.Abs(StableHash(code));
        var compiled = await Task.Run(() => IlWorkbench.Compile(code, workDir, name));
        if (!compiled.Success)
        {
            if (Trace) Console.WriteLine($"      [{slug}/{ge.Concept}] компиляция: {compiled.Diagnostics.Replace("\n", " | ")}");
            return null;
        }

        var run = await Task.Run(() => SandboxRunner.Run(compiled.AssemblyPath!, 12000, workDir));
        if (run.TimedOut || (run.ExitCode != 0 && run.Output.Trim().Length == 0))
        {
            if (Trace) Console.WriteLine($"      [{slug}/{ge.Concept}] запуск: exit={run.ExitCode} {run.Error.Replace("\n", " | ")}");
            return null;
        }

        var output = run.Output.Trim();
        if (output.Length == 0) output = EmptyOutput;

        return new GeneratedExample
        {
            Concept = ge.Concept,
            Format = ge.Format,
            Prompt = ge.Prompt,
            Code = ge.Code,
            HarnessCode = ge.HarnessCode,
            Options = new List<string> { output },
            CorrectAnswer = output,
            Explanation = ge.Explanation + $"\n\nЭталон получен запуском этого кода: вывод «{output}».",
            NeedsVerification = false,
            IsGui = ge.IsGui,
        };
    }

    static int Words(string text) =>
        text.Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Стабильный id урока из slug (2.12.php → 2012): прогресс переживает любую пересборку.</summary>
    static int LessonIdOf(string slug)
    {
        var core = slug.Replace(".php", "");
        var parts = core.Split('.');
        if (parts.Length == 2 && int.TryParse(parts[0], out var ch) && int.TryParse(parts[1], out var no))
            return ch * 1000 + no;
        return 900_000 + Math.Abs(StableHash(core));
    }

    static int StableHash(string s)
    {
        unchecked { int h = 17; foreach (var c in s) h = h * 31 + c; return h; }
    }

    static string GuessConcept(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("класс") || t.Contains("объект")) return "Класс";
        if (t.Contains("метод") || t.Contains("return")) return "Метод";
        if (t.Contains("цикл")) return "Цикл";
        if (t.Contains("строк") || t.Contains("string")) return "Строка и интерполяция";
        if (t.Contains("наслед") || t.Contains("виртуальн") || t.Contains("абстракт")) return "Наследование";
        if (t.Contains("интерфейс")) return "Интерфейс";
        if (t.Contains("исключ")) return "Исключение";
        if (t.Contains("делегат") || t.Contains("лямбд") || t.Contains("событ")) return "Делегат";
        if (t.Contains("переменн")) return "Переменная";
        if (t.Contains("тип") || t.Contains("преобразов")) return "Тип данных";
        if (t.Contains("массив")) return "Массив";
        if (t.Contains("if") || t.Contains("условн") || t.Contains("switch")) return "Условие if/else";
        if (t.Contains("linq")) return "LINQ";
        if (t.Contains("поток") || t.Contains("async") || t.Contains("task")) return "Многопоточность";
        if (t.Contains("коллекц") || t.Contains("list") || t.Contains("словарь")) return "Словарь и коллекции";
        return title;
    }

    static void GzipFile(string source, string dest)
    {
        using var fs = File.OpenRead(source);
        using var gz = new GZipStream(File.Create(dest), CompressionLevel.SmallestSize);
        fs.CopyTo(gz);
    }

    static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
    }

    static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("CsharpLearning*.sln*").Length > 0) return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
