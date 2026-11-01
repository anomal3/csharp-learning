using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CsharpLearning.App.Services;

public sealed record UpdateInfo(int ContentVersion, DateTime BuiltAt, int Lessons, int Questions, string FileName, string Sha256, long Size)
{
    public bool IsNewerThan(int knownVersion) => ContentVersion > knownVersion;
}

public sealed record UpdateResult(bool Ok, bool Updated, string Message, UpdateInfo? Info);

/// <summary>
/// Самообновление учебного контента. Приложение скачивает с хостинга
/// https://blueboxproduction.ru/csharp/ два файла:
///   manifest.json  — версия контента, имя пакета и его sha256
///   course-vN.db.gz — сжатая база уроков
/// Проверка по sha256 защищает от битого/подменённого пакета. Прогресс ученика (progress.db)
/// при обновлении не трогается.
/// </summary>
public static class UpdateService
{
    public static readonly string BaseUrl = Environment.GetEnvironmentVariable("CSHARP_LEARNING_FEED")
        ?? "https://blueboxproduction.ru/csharp/";

    static readonly HttpClient Http = Create();

    static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CsharpLearningApp/1.0 (offline course updater)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json,application/octet-stream,*/*");
        return c;
    }

    public static async Task<UpdateResult> CheckAndInstallAsync(int knownVersion, bool install, CancellationToken ct = default)
    {
        UpdateInfo info;
        try
        {
            info = await FetchManifestAsync(ct);
        }
        catch (HttpRequestException ex) { return new UpdateResult(false, false, "Сеть недоступна: " + ex.Message, null); }
        catch (TaskCanceledException) { return new UpdateResult(false, false, "Хостинг не ответил за отведённое время.", null); }
        catch (Exception ex) { return new UpdateResult(false, false, "Не удалось прочитать manifest.json: " + ex.Message, null); }

        if (!info.IsNewerThan(knownVersion))
            return new UpdateResult(true, false, $"Обновлений нет. На хостинге версия {info.ContentVersion}, у вас {knownVersion}.", info);

        if (!install)
            return new UpdateResult(true, false, $"Есть обновление до версии {info.ContentVersion} ({info.Lessons} уроков).", info);

        try
        {
            var (dbFile, packageFile) = await DownloadPackageAsync(info, ct);
            var courseDb = Paths.CourseDb;

            // Подмена/битость пакета проверяются ДО замены рабочей базы:
            // хэшируем скачанный пакет, а не распакованную базу — ContentBuilder считает sha256
            // именно по файлу, который выкладывает на хостинг.
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packageFile)));
            if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase))
                return new UpdateResult(false, false, "Контрольная сумма пакета не совпала — обновление отменено.", info);

            // Контент меняется, прогресс остаётся: course.db и progress.db — разные файлы.
            var tmp = courseDb + ".new";
            if (File.Exists(tmp)) File.Delete(tmp);
            File.Move(dbFile, tmp);

            foreach (var side in new[] { courseDb + "-wal", courseDb + "-shm" })
                if (File.Exists(side)) File.Delete(side);

            // Файл должен оказаться читаемой базой, иначе курс не открыть: проверяем до замены.
            if (!LooksLikeSqliteDb(tmp))
            {
                File.Delete(tmp);
                return new UpdateResult(false, false, "Пакет распаковался не в SQLite-базу — обновление отменено.", info);
            }

            // course.db закрыт (LearningState.CloseCourse) — замена безопасна.
            if (File.Exists(courseDb)) File.Replace(tmp, courseDb, courseDb + ".prev");
            else File.Move(tmp, courseDb);

            // course.db.prev — страховка на случай, если следующая версия окажется хуже.
            try { if (File.Exists(packageFile)) File.Delete(packageFile); } catch { }

            return new UpdateResult(true, true,
                $"Контент обновлён до версии {info.ContentVersion}: {info.Lessons} уроков, {info.Questions} вопросов. Прежняя база сохранена как course.db.prev.", info);
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, false, "Не удалось установить обновление: " + ex.Message, info);
        }
    }

    // ---------------- обновление самой программы (установщик) ----------------

    /// <summary>Версия запущенной программы (из сборки).</summary>
    public static Version CurrentVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    /// <summary>Описание нового установщика из блока "app" в manifest.json.</summary>
    public sealed record AppUpdateInfo(Version Version, string FileName, string Sha256, long Size);

    /// <summary>Есть ли на хостинге установщик новее запущенной программы; null — нет или хостинг недоступен.</summary>
    public static async Task<AppUpdateInfo?> FetchAppUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await Http.GetAsync(new Uri(BaseUrl.TrimEnd('/') + "/manifest.json"), ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse((await resp.Content.ReadAsStringAsync(ct)).TrimStart('﻿'));
            if (!doc.RootElement.TryGetProperty("app", out var app)) return null;

            if (!Version.TryParse(app.GetProperty("version").GetString(), out var ver)) return null;
            var info = new AppUpdateInfo(ver, app.GetProperty("file").GetString() ?? "",
                app.TryGetProperty("sha256", out var h) ? h.GetString() ?? "" : "",
                app.TryGetProperty("size", out var s) ? s.GetInt64() : 0);
            return ver > CurrentVersion && info.FileName.Length > 0 ? info : null;
        }
        catch { return null; }
    }

    /// <summary>Скачивает установщик и сверяет sha256; возвращает путь к файлу или null с причиной в error.</summary>
    public static async Task<(string? Path, string Error)> DownloadInstallerAsync(AppUpdateInfo info, CancellationToken ct = default)
    {
        try
        {
            Directory.CreateDirectory(Paths.UpdateCache);
            string target = System.IO.Path.Combine(Paths.UpdateCache, System.IO.Path.GetFileName(info.FileName));
            if (File.Exists(target)) File.Delete(target);

            using (var resp = await Http.GetAsync(new Uri(BaseUrl.TrimEnd('/') + "/" + info.FileName), HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var remote = await resp.Content.ReadAsStreamAsync(ct);
                await using var disk = File.Create(target);
                await remote.CopyToAsync(disk, ct);
            }

            string sha;
            await using (var check = File.OpenRead(target)) sha = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));
            if (!string.Equals(sha, info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(target);
                return (null, "Контрольная сумма установщика не совпала — обновление отменено.");
            }
            return (target, "");
        }
        catch (Exception ex) { return (null, "Не удалось скачать установщик: " + ex.Message); }
    }

    /// <summary>Тихий запуск установщика: он сам закроет программу, обновит файлы и запустит её снова.</summary>
    public static void LaunchInstaller(string path) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = true });

    /// <summary>Магическая голова SQLite-файла: «SQLite format 3\0».</summary>
    static bool LooksLikeSqliteDb(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[16];
            if (fs.Read(head) < 16) return false;
            return System.Text.Encoding.ASCII.GetString(head).StartsWith("SQLite format 3");
        }
        catch { return false; }
    }

    /// <summary>Откат на прежнюю базу (course.db.prev), если новая версия не устроила.</summary>
    public static bool Rollback()
    {
        string courseDb = Paths.CourseDb, prev = Paths.CourseDb + ".prev";
        if (!File.Exists(prev)) return false;

        try
        {
            foreach (var side in new[] { courseDb + "-wal", courseDb + "-shm" })
                if (File.Exists(side)) File.Delete(side);

            File.Replace(prev, courseDb, null);
            return true;
        }
        catch { return false; }
    }

    static async Task<UpdateInfo> FetchManifestAsync(CancellationToken ct)
    {
        using var resp = await Http.GetAsync(new Uri(BaseUrl.TrimEnd('/') + "/manifest.json"), ct);
        resp.EnsureSuccessStatusCode();
        var json = (await resp.Content.ReadAsStringAsync(ct)).TrimStart('﻿');

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int version = root.TryGetProperty("contentVersion", out var v) ? v.GetInt32() : 0;
        DateTime built = root.TryGetProperty("builtAt", out var b) && DateTime.TryParse(b.GetString(), out var dt) ? dt : DateTime.UtcNow;
        int lessons = root.TryGetProperty("lessons", out var l) ? l.GetInt32() : 0;
        int questions = root.TryGetProperty("questions", out var q) ? q.GetInt32() : 0;

        var file = root.TryGetProperty("files", out var fs) && fs.ValueKind == JsonValueKind.Array && fs.GetArrayLength() > 0
            ? fs[0] : default;

        if (file.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException("manifest.json не содержит описания файла");

        return new UpdateInfo(
            version,
            built,
            lessons,
            questions,
            file.GetProperty("name").GetString() ?? "",
            file.TryGetProperty("sha256", out var h) ? (h.GetString() ?? "") : "",
            file.TryGetProperty("size", out var s) ? s.GetInt64() : 0);
    }

    /// <summary>
    /// Скачиваем пакет КАК ЕСТЬ (course-vN.db.gz) и только потом распаковываем.
    /// Именно байты пакета хэширует ContentBuilder в manifest.json, поэтому сверять sha256
    /// надо по скачанному .gz, а не по распакованной базе — иначе контрольная сумма никогда
    /// не совпадёт и обновление всегда отклонялось бы как «битое».
    /// </summary>
    static async Task<(string DbPath, string PackagePath)> DownloadPackageAsync(UpdateInfo info, CancellationToken ct)
    {
        Directory.CreateDirectory(Paths.UpdateCache);
        bool packed = info.FileName.EndsWith(".gz");

        string raw = Path.Combine(Paths.UpdateCache, "download-" + info.ContentVersion + (packed ? ".db.gz" : ".db"));
        string target = Path.Combine(Paths.UpdateCache, "download-" + info.ContentVersion + ".db");
        if (File.Exists(raw)) File.Delete(raw);
        if (File.Exists(target)) File.Delete(target);

        using (var resp = await Http.GetAsync(new Uri(BaseUrl.TrimEnd('/') + "/" + info.FileName), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var remote = await resp.Content.ReadAsStreamAsync(ct);
            await using var disk = File.Create(raw);
            await remote.CopyToAsync(disk, ct);
        }

        if (packed)
        {
            await using var src = File.OpenRead(raw);
            await using var gz = new GZipStream(src, CompressionMode.Decompress);
            await using var outDb = File.Create(target);
            await gz.CopyToAsync(outDb, ct);
        }

        // PackagePath — тот файл, байты которого хэшировал ContentBuilder:
        // скачанный как есть (.gz), либо сам .db, если пакет не сжат.
        return packed ? (target, raw) : (target, target);
    }
}
