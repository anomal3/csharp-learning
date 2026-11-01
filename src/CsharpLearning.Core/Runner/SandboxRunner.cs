using System.Diagnostics;
using System.Text;

namespace CsharpLearning.Core.Runner;

public sealed record RunOutcome(bool Success, string Output, string Error, int ExitCode, double ElapsedMs, bool TimedOut);

/// <summary>
/// Запускает скомпилированную сборку в ОТДЕЛЬНОМ процессе: бесконечный цикл или падение
/// не подвешивают учебное приложение — процесс убивается по таймауту.
/// </summary>
public static class SandboxRunner
{
    static SandboxRunner()
    {
        // OEM-кодировки (866/1251) живут в System.Text.Encoding.CodePages.
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* уже зарегистрировано */ }
    }

    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Кодировки, в которые дочерний .NET-процесс реально пишет консоль на Windows.</summary>
    static readonly int[] ConsoleCodePages = { 866, 1251 };

    /// <summary>
    /// workDir — рабочая папка запускаемого примера. Учёбные примеры пишут/читают файлы
    /// «по имени из текущей папки»; без изоляции они гадили бы в корень проекта и мешали
    /// друг другу (IOException «файл используется другим процессом»).
    /// </summary>
    public static RunOutcome Run(string assemblyPath, int timeoutMs = 6000, string? workDir = null, int maxOutputChars = 4000)
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (string.IsNullOrWhiteSpace(host))
        {
            // без SDK переменной DOTNET_HOST_PATH нет — берём dotnet.exe, который ставит рантайм
            var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            host = File.Exists(installed) ? installed : "dotnet";
        }

        var psi = new ProcessStartInfo
        {
            FileName = host,
            Arguments = $"exec \"{assemblyPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Учебные примеры иногда читают с клавиатуры. Песочница без stdin
            // сразу даёт конец ввода — иначе пример висел бы до самого таймаута.
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workDir))
        {
            try
            {
                Directory.CreateDirectory(workDir);
                psi.WorkingDirectory = workDir;
            }
            catch { /* запуск всё равно состоится — просто в текущей папке */ }
        }

        // КОДИРОВКА ВЫВОДА. Дочерний .NET-процесс пишет перенаправленную консоль в OEM-
        // кодировке Windows (на русской системе — cp866), а не в UTF-8. Если читать поток
        // как UTF-8, кириллица превращается в U+FFFD («????»), и эталон ответа порчен:
        // ученик видит кашу, а правильный ответ не засчитывается.
        // Поэтому байты собираются как есть, а кодировка определяется по самим байтам.
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "0";

        var sw = Stopwatch.StartNew();

        try
        {
            using var p = Process.Start(psi)!;

            // Читаем БЙТЫ, а не «строки в кодировке»: кодировку дочернего процесса
            // угадываем по содержимому (см. DecodeConsoleBytes).
            var outTask = Task.Run(() => ReadAllBytes(p.StandardOutput.BaseStream, maxOutputChars * 4));
            var errTask = Task.Run(() => ReadAllBytes(p.StandardError.BaseStream, maxOutputChars * 4));

            try { p.StandardInput.Close(); } catch { /* пример не читает ввод */ }

            bool exited = p.WaitForExit(timeoutMs);
            if (!exited) TryKill(p);

            // WaitForExit(таймаут) возвращает управление раньше, чем доедут последние байты
            // потока вывода — процесс уже мёртв, поэтому второй WaitForExit() почти мгновенно
            // дожимает остаток. Без него эталон мог получиться обрезанным или пустым.
            try { p.WaitForExit(); } catch { /* процесс уже завершён */ }

            try { Task.WaitAll(new[] { outTask, errTask }, 3000); }
            catch { /* поток оборвался на Kill — читаем что доехало */ }

            string output = Trim(DecodeConsoleBytes(SafeResult(outTask)), maxOutputChars);
            string error = Trim(DecodeConsoleBytes(SafeResult(errTask)), maxOutputChars);

            if (!exited)
                return new RunOutcome(false, output,
                    string.IsNullOrWhiteSpace(error)
                        ? "Программа не завершилась за " + timeoutMs + " мс — вероятен бесконечный цикл или взаимный deadlock."
                        : error,
                    -1, sw.Elapsed.TotalMilliseconds, true);

            return new RunOutcome(p.ExitCode == 0, output, error, p.ExitCode, sw.Elapsed.TotalMilliseconds, false);
        }
        catch (Exception ex)
        {
            return new RunOutcome(false, "", ex.Message, -2, sw.Elapsed.TotalMilliseconds, false);
        }

        static byte[] SafeResult(Task<byte[]> t) => t.IsCompletedSuccessfully ? t.Result : Array.Empty<byte>();

        static byte[] ReadAllBytes(Stream stream, int capBytes)
        {
            using var ms = new MemoryStream();
            var buf = new byte[4096];
            try
            {
                int n;
                while ((n = stream.Read(buf, 0, buf.Length)) > 0)
                {
                    if (ms.Length < capBytes)
                        ms.Write(buf, 0, Math.Min(n, capBytes - (int)ms.Length));
                }
            }
            catch { /* процесс убит по таймауту — поток закрыт, берём что было */ }
            return ms.ToArray();
        }

        static string Trim(string s, int maxOutputChars)
        {
            return s.Length > maxOutputChars ? s[..maxOutputChars] + "\n… вывод обрезан" : s;
        }

        /// <summary>
        /// Расшифровать вывод дочернего процесса. .NET пишет перенаправленную консоль в кодовой
        /// странице Windows (русская система — cp866), поэтому UTF-8-читатель давал U+FFFD на всей
        /// кириллице. Сначала пробуем строгий UTF-8, потом выбираем из cp866/cp1251 ту страницу,
        /// которая даёт русские буквы, а не «Ć ¢ ѓ Џ».
        /// </summary>
        static string DecodeConsoleBytes(byte[] bytes)
        {
            if (bytes.Length == 0) return "";

            try { return StrictUtf8.GetString(bytes); } catch { /* не UTF-8 */ }

            string best = "";
            int bestScore = int.MinValue;
            foreach (var cp in ConsoleCodePages)
            {
                string s;
                try { s = Encoding.GetEncoding(cp).GetString(bytes); }
                catch { continue; }

                int score = RussianScore(s);
                if (score > bestScore) { bestScore = score; best = s; }
            }

            if (best.Length > 0) return best;
            return Encoding.Default.GetString(bytes);
        }

        /// <summary>Сколько декодированный текст похож на нормальную кириллицу.</summary>
        static int RussianScore(string s)
        {
            int good = 0, bad = 0;
            foreach (var c in s)
            {
                if (c < 0x80) { if (!char.IsControl(c)) good++; continue; }
                // русские буквы (А–Я, а–я) + Ё/ё
                if (c is >= '\u0410' and <= '\u042F' || c is >= '\u0430' and <= '\u044F' || c is '\u0401' or '\u0451')
                    good += 2;
                else if (c == '\uFFFD' || char.IsControl(c)) bad += 3;
                else bad += 1; // служебные символы чужой codepage
            }
            return good - bad;
        }

        static void TryKill(Process p)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
        }
    }
}
