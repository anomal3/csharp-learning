using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CsharpLearning.Core.Runner;

/// <summary>Итог запуска Rust-кода.</summary>
/// <param name="Compiled">false — не скомпилировалось: текст ошибок в <see cref="Stderr"/>.</param>
/// <param name="Via">«rustc» — компилятор на этом ПК, «playground» — play.rust-lang.org (нужен интернет).</param>
public sealed record RustRunResult(bool Compiled, string Stdout, string Stderr, int ExitCode, long Ms, bool TimedOut, string Via)
{
    public bool Ok => Compiled && !TimedOut && ExitCode == 0;
}

/// <summary>
/// Запуск Rust-примеров. Сначала локальный rustc (быстро, офлайн), иначе — play.rust-lang.org.
/// Фрагменты без fn main оборачиваются в main: Rust допускает объявления функций, структур, impl и use внутри функции.
/// </summary>
public static class RustRunner
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(40) };
    static string? _rustc;
    static bool _rustcSearched;

    /// <summary>Путь к rustc.exe или null, если компилятора на ПК нет.</summary>
    public static string? FindRustc()
    {
        if (_rustcSearched) return _rustc;
        _rustcSearched = true;

        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("CSHARP_LEARNING_RUSTC");
        if (!string.IsNullOrWhiteSpace(env)) candidates.Add(env);

        string exeName = OperatingSystem.IsWindows() ? "rustc.exe" : "rustc";
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Настоящий rustc из тулчейна rustup. В .cargo\bin лежит прокси (на Windows часто символическая ссылка на rustup.exe):
        // из другого процесса её запуск может упасть с «недоверенная точка подключения», а сам прокси ещё и медленнее.
        try
        {
            string rustupHome = Environment.GetEnvironmentVariable("RUSTUP_HOME") is { Length: > 0 } rh ? rh : Path.Combine(home, ".rustup");
            string toolchains = Path.Combine(rustupHome, "toolchains");
            if (Directory.Exists(toolchains))
                candidates.AddRange(Directory.GetDirectories(toolchains)
                    .OrderBy(d => Path.GetFileName(d).StartsWith("stable", StringComparison.OrdinalIgnoreCase) ? 0 : 1)   // stable раньше nightly
                    .Select(d => Path.Combine(d, "bin", exeName)));
        }
        catch { }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try { candidates.Add(Path.Combine(dir.Trim('"'), exeName)); } catch { }
        }
        candidates.Add(Path.Combine(home, ".cargo", "bin", exeName));

        _rustc = candidates.FirstOrDefault(File.Exists);
        return _rustc;
    }

    public static bool HasLocalCompiler => FindRustc() is not null;

    /// <summary>Исходник для запуска: если нет fn main, весь фрагмент кладётся внутрь main.</summary>
    public static string Prepare(string code)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"\bfn\s+main\s*\(")) return code;
        return "fn main() {\n" + code + "\n}\n";
    }

    public static async Task<RustRunResult> RunAsync(string code, string workRoot, TimeSpan? runTimeout = null, CancellationToken ct = default)
    {
        string source = Prepare(code);
        var rustc = FindRustc();
        if (rustc is not null)
        {
            // Windows может не дать запустить процесс: «недоверенная точка подключения» (путь через перенаправленную/виртуализированную папку),
            // ссылка, антивирус. Пробуем другую рабочую папку (временную), и только потом уходим на Playground.
            foreach (var root in new[] { workRoot, Path.Combine(Path.GetTempPath(), "CsharpLearning-run") }.Distinct())
            {
                try { return await RunLocalAsync(rustc, source, root, runTimeout ?? TimeSpan.FromSeconds(8), ct); }
                catch (System.ComponentModel.Win32Exception) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _rustc = null;   // дальше не мучаем локальный компилятор, пока приложение не перезапущено
        }
        return await RunPlaygroundAsync(source, ct);
    }

    // ---------- локальный rustc ----------

    static async Task<RustRunResult> RunLocalAsync(string rustc, string source, string workRoot, TimeSpan runTimeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        string dir = Path.Combine(workRoot, "rust-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(dir);
        try
        {
            string main = Path.Combine(dir, "main.rs");
            string exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "main.exe" : "main");
            await File.WriteAllTextAsync(main, source, new UTF8Encoding(false), ct);

            // -A warnings: ученику нужны ошибки, а не предупреждения о неиспользуемых переменных
            var compile = await StartAsync(rustc, $"--edition 2021 --color never -A warnings -o \"{exe}\" \"{main}\"", dir, TimeSpan.FromSeconds(90), ct);
            if (compile.TimedOut || compile.ExitCode != 0 || !File.Exists(exe))
                return new RustRunResult(false, "", Tidy(compile.Stderr + compile.Stdout), compile.ExitCode, sw.ElapsedMilliseconds, compile.TimedOut, "rustc");

            var run = await StartAsync(exe, "", dir, runTimeout, ct);
            return new RustRunResult(true, run.Stdout, run.Stderr, run.ExitCode, sw.ElapsedMilliseconds, run.TimedOut, "rustc");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    sealed record Proc(string Stdout, string Stderr, int ExitCode, bool TimedOut);

    static async Task<Proc> StartAsync(string file, string args, string workDir, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();                                       // программа не ждёт ввода с клавиатуры

        var outTask = ReadCappedAsync(p.StandardOutput);
        var errTask = ReadCappedAsync(p.StandardError);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        bool timedOut = false;
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested;
            try { p.Kill(true); } catch { }
            if (ct.IsCancellationRequested) throw;
        }

        return new Proc(await outTask, await errTask, timedOut ? -1 : p.ExitCode, timedOut);
    }

    /// <summary>Читает поток, но не больше ~64 КБ: бесконечный println! не должен съесть память.</summary>
    static async Task<string> ReadCappedAsync(StreamReader reader)
    {
        var sb = new StringBuilder();
        var buf = new char[4096];
        int n;
        while ((n = await reader.ReadAsync(buf, 0, buf.Length)) > 0)
            if (sb.Length < 65536) sb.Append(buf, 0, n);
        return sb.ToString();
    }

    static string Tidy(string s) => s.Replace("\r\n", "\n").Trim();

    // ---------- play.rust-lang.org ----------

    static async Task<RustRunResult> RunPlaygroundAsync(string source, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                channel = "stable", mode = "debug", edition = "2021", crateType = "bin", tests = false, code = source, backtrace = false,
            });
            using var resp = await Http.PostAsync("https://play.rust-lang.org/execute", new StringContent(body, Encoding.UTF8, "application/json"), ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            string stdout = root.TryGetProperty("stdout", out var o) ? o.GetString() ?? "" : "";
            string stderr = root.TryGetProperty("stderr", out var e) ? e.GetString() ?? "" : "";

            // на Playground компиляция и запуск в одном ответе: ошибки компилятора содержат «error[E…]» / «could not compile»
            bool compiled = success || !(stderr.Contains("error[") || stderr.Contains("could not compile") || stderr.Contains("error:"));
            return new RustRunResult(compiled, stdout, compiled ? FilterCargoNoise(stderr) : Tidy(stderr), success ? 0 : 1, sw.ElapsedMilliseconds, false, "playground");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new RustRunResult(false, "", "Нет компилятора Rust на этом компьютере и нет доступа к play.rust-lang.org. " +
                "Установите Rust (https://rustup.rs) или подключитесь к интернету.", -1, sw.ElapsedMilliseconds, false, "playground");
        }
    }

    // Playground в stderr добавляет строки «Compiling playground…», «Finished…», «Running…» — ученику они не нужны
    static string FilterCargoNoise(string stderr)
    {
        var keep = stderr.Replace("\r\n", "\n").Split('\n')
            .Where(l => !(l.TrimStart().StartsWith("Compiling playground") || l.TrimStart().StartsWith("Finished ")
                          || l.TrimStart().StartsWith("Running `target")));
        return string.Join("\n", keep).Trim();
    }
}
