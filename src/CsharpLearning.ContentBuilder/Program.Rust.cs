using CsharpLearning.Core.Model;
using CsharpLearning.Core.Pedagogy;
using CsharpLearning.Core.Runner;
using CsharpLearning.Core.Storage;

namespace CsharpLearning.ContentBuilder;

/// <summary>Часть сборщика, которая знает про Rust: что считать запускаемым примером и как получать эталон.</summary>
internal static partial class Program
{
    /// <summary>Примеры, которым нужен внешний мир или чей вывод нестабилен — их проверить запуском нельзя.</summary>
    static readonly string[] RustSkip =
    {
        "stdin", "read_line", "std::fs", "File::", "OpenOptions", "std::env", "std::net", "TcpListener", "TcpStream",
        "std::process", "Command::new", "SystemTime", "Instant::now", "rand::", "thread::spawn", "thread::sleep",
        "std::io::Read", "extern \"C\"", "#[no_mangle]", "unsafe extern", "use libc", "cc::",
        "proc_macro", "#![", "#[test]", "#[cfg(test)]", "mod tests",
    };

    static bool LooksRunnableRust(string code)
    {
        if (code.Length < 14 || code.Length > 1800) return false;
        if (!(code.Contains("println!") || code.Contains("print!"))) return false;
        if (code.TrimEnd().EndsWith("...") || code.Contains("…") || code.Contains("/* ... */")) return false;
        return !NeedsOutsideWorldRust(code);
    }

    static bool NeedsOutsideWorldRust(string code)
    {
        foreach (var s in RustSkip)
            if (code.Contains(s)) return true;

        // жёсткие пути из учебника и внешние крейты (use serde::…, extern crate …)
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"[""'][A-Za-z]:[\\/]")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(code, @"^\s*(use|extern crate)\s+(?!std\b|core\b|alloc\b|crate\b|self\b|super\b)", System.Text.RegularExpressions.RegexOptions.Multiline)) return true;
        return false;
    }

    /// <summary>
    /// Эталон «что выведет» = реальный запуск rustc. Пример запускается ДВАЖДЫ: порядок обхода HashMap/HashSet
    /// и подобное в Rust меняется от запуска к запуску, и такой вывод эталоном быть не может.
    /// </summary>
    static async Task<GeneratedExample?> VerifyRustExample(GeneratedExample ge, string workDir, string slug)
    {
        string code = ge.Code;
        if (string.IsNullOrWhiteSpace(code)) return ge.Format == QuizFormat.MultipleChoice ? ge : null;

        var first = await RustRunner.RunAsync(code, workDir, TimeSpan.FromSeconds(6));
        if (!first.Compiled)
        {
            if (Trace) Console.WriteLine($"      [{slug}/{ge.Concept}] компиляция: {first.Stderr.Split('\n').FirstOrDefault()}");
            return null;
        }
        if (first.TimedOut || first.ExitCode != 0)
        {
            if (Trace) Console.WriteLine($"      [{slug}/{ge.Concept}] запуск: exit={first.ExitCode} timeout={first.TimedOut} {first.Stderr.Split('\n').FirstOrDefault()}");
            return null;
        }

        var second = await RustRunner.RunAsync(code, workDir, TimeSpan.FromSeconds(6));
        if (!second.Ok || Norm(second.Stdout) != Norm(first.Stdout))
        {
            if (Trace) Console.WriteLine($"      [{slug}/{ge.Concept}] вывод нестабилен или второй запуск упал");
            return null;
        }

        var output = Norm(first.Stdout);
        if (output.Length == 0) output = EmptyOutput;

        return new GeneratedExample
        {
            Concept = ge.Concept,
            Format = ge.Format,
            Prompt = ge.Prompt,
            Code = ge.Code,
            HarnessCode = null,
            Options = new List<string> { output },
            CorrectAnswer = output,
            Explanation = ge.Explanation + $"\n\nЭталон получен запуском этого кода: вывод «{output}».",
            NeedsVerification = false,
            IsGui = false,
        };

        static string Norm(string s) => s.Replace("\r\n", "\n").Trim();
    }

    /// <summary>Повторная проверка сохранённых эталонов Rust-курса: заново запускаем код вопроса.</summary>
    static async Task<int> CheckRustExamplesAsync(ContentDb db)
    {
        var workDir = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "check-rust");
        if (Directory.Exists(workDir)) Directory.Delete(workDir, true);
        Directory.CreateDirectory(workDir);

        var targets = new List<QuizItem>();
        foreach (var ch in db.LoadChapters())
            foreach (var lref in ch.Lessons)
            {
                var lesson = db.LoadLesson(lref.Id);
                if (lesson is null) continue;
                targets.AddRange(lesson.Questions.Where(q => q.Format == QuizFormat.PredictOutput && !string.IsNullOrWhiteSpace(q.Code)));
            }

        var gate = new SemaphoreSlim(4);
        var bad = new List<(int id, string reason)>();

        await Task.WhenAll(targets.Select(async q =>
        {
            await gate.WaitAsync();
            try
            {
                var run = await RustRunner.RunAsync(q.Code, workDir, TimeSpan.FromSeconds(6));
                string actual = run.Stdout.Replace("\r\n", "\n").Trim();
                if (actual.Length == 0) actual = EmptyOutput;
                string expected = q.CorrectAnswer.Replace("\r\n", "\n").Trim();

                string? reason = !run.Compiled ? "не компилируется" : run.TimedOut ? "не завершилась" : run.ExitCode != 0 ? "паника" : actual != expected ? "вывод отличается" : null;
                if (reason is not null) lock (bad) bad.Add((q.Id, reason));
            }
            finally { gate.Release(); }
        }));

        foreach (var (id, reason) in bad)
        {
            if (Trace) Console.WriteLine($"  ✗ вопрос {id}: {reason}");
            db.RemoveQuestion(id);
        }
        Console.WriteLine($"  проверено вопросов с кодом: {targets.Count}, отклонено: {bad.Count}");
        return bad.Count == 0 ? 0 : 2;
    }
}
