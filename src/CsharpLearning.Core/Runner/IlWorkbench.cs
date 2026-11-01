using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

namespace CsharpLearning.Core.Runner;

public sealed record CompileResult(bool Success, string? AssemblyPath, string Diagnostics, string Source, string Framework);

/// <summary>
/// Превращает учебный пример в настоящую сборку: C# → CIL → машинный код при запуске.
///
/// Важно: компилируем против REF-паков (.NET reference assemblies), а запускаем против
/// обычного shared-рантайма. Если брать dll'ы из shared\...\runtime, компилятор падает
/// на нативных сборках (wpfgfx_cor3.dll, msquic.dll и т.п.) — «Образ среды предустановки
/// не содержит управляемые метаданные».
/// </summary>
public static class IlWorkbench
{
    public const string ConsoleFramework = "Microsoft.NETCore.App";
    public const string DesktopFramework = "Microsoft.WindowsDesktop.App";

    static readonly Lazy<string[]> CoreRefDirs = new(LocateCoreRefs);
    static readonly Lazy<string[]> DesktopRefDirs = new(LocateDesktopRefs);
    static readonly Lazy<string> RuntimeVersion = new(LocateRuntimeVersion);

    // ---------------- где брать ссылки ----------------
    static string DotnetRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv)) return fromEnv!;

        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(host))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(host!));
            if (dir is not null && Directory.Exists(dir)) return dir;
        }

        var self = Path.GetDirectoryName(typeof(object).Assembly.Location);
        for (int i = 0; i < 4 && self is not null; i++)
        {
            var packs = Path.Combine(self, "packs");
            if (Directory.Exists(packs)) return self;
            self = Path.GetDirectoryName(self);
        }

        return @"C:\Program Files\dotnet";
    }

    static string[] LocateCoreRefs() => Bundled("core") ?? RefPaths("Microsoft.NETCore.App.Ref", requireDesktop: false);
    static string[] LocateDesktopRefs() => Bundled("desktop") ?? RefPaths("Microsoft.WindowsDesktop.App.Ref", requireDesktop: true);

    /// <summary>
    /// Reference-сборки, положенные рядом с программой (refs\core, refs\desktop): так примеры
    /// компилируются и у ученика, у которого установлен только рантайм, а SDK (packs) нет.
    /// </summary>
    static string[]? Bundled(string kind)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "refs", kind);
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "*.dll");
        return files.Length > 0 ? files : null;
    }

    static string[] RefPaths(string pack, bool requireDesktop)
    {
        var baseDir = Path.Combine(DotnetRoot(), "packs", pack);
        if (!Directory.Exists(baseDir)) return Array.Empty<string>();

        // самая свежая версия пака, у которой есть ref/<tfm>
        foreach (var version in Directory.GetDirectories(baseDir).OrderByDescending(d => d, VersionComparer.Instance))
        {
            var refRoot = Path.Combine(version, "ref");
            if (!Directory.Exists(refRoot)) continue;

            var tfm = Directory.GetDirectories(refRoot).OrderByDescending(d => d, VersionComparer.Instance)
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("net"));
            if (tfm is null) continue;

            var files = Directory.GetFiles(tfm, "*.dll");
            if (files.Length == 0) continue;
            return files;
        }

        _ = requireDesktop;
        return Array.Empty<string>();
    }

    /// <summary>Версия рантайма для runtimeconfig.json — берём из установленного shared-рантайма.</summary>
    static string LocateRuntimeVersion()
    {
        var shared = Path.Combine(DotnetRoot(), "shared", ConsoleFramework);
        if (Directory.Exists(shared))
        {
            var v = Directory.GetDirectories(shared).Select(Path.GetFileName)
                .Where(n => n is not null && Version.TryParse(n, out _))
                .OrderByDescending(n => n, VersionComparer.Instance)
                .FirstOrDefault();
            if (v is not null) return v;
        }
        return Environment.Version.ToString();
    }

    public static IEnumerable<MetadataReference> References(string framework)
    {
        var dirs = framework == DesktopFramework
            ? CoreRefDirs.Value.Concat(DesktopRefDirs.Value)
            : CoreRefDirs.Value;

        foreach (var f in dirs)
            yield return MetadataReference.CreateFromFile(f);
    }

    /// <summary>
    /// GUI-пример? WinForms/WPF-типы встречаются в тексте примера — такому примеру нужен
    /// desktop-рантайм, иначе он не запустится.
    /// </summary>
    public static string DetectFramework(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return ConsoleFramework;
        var t = code;
        if (t.Contains("System.Windows.Forms") || t.Contains("Windows.Forms") || t.Contains("MessageBox.Show") ||
            t.Contains("Label") && t.Contains("Form") || t.Contains("button1") || t.Contains("TextBox"))
            return DesktopFramework;
        if (t.Contains("System.Windows.Application") || t.Contains("System.Windows.Window") ||
            t.Contains("System.Windows.Controls") || t.Contains("DispatcherTimer"))
            return DesktopFramework;
        return ConsoleFramework;
    }

    public const string UsingsBlock = """
        global using System;
        global using System.IO;
        global using System.Linq;
        global using System.Collections.Generic;
        global using System.Collections.Concurrent;
        global using System.Text;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using System.Text.RegularExpressions;
        """;

    /// <summary>
    /// Учебник пишет примеры «как удобно»: сначала класс, потом вызовы, локальные функции.
    /// Компилятор требует точку входа и порядок «операторы до объявлений типов» (CS8803).
    /// Исходник разбирается как дерево Roslyn, затем аккуратно упаковывается в class Program.Main.
    /// Ученик в расширенном режиме видит и исходник, и результат упаковки.
    /// </summary>
    /// <summary>Служебный хвост: консоль примера пишет в UTF-8, чтобы кириллица не портилась. Ученику не показывается.</summary>
    public const string HiddenMarker = "// <<служебное>>";

    const string HiddenInit = HiddenMarker + """

        file static class __ConsoleInit
        {
            [System.Runtime.CompilerServices.ModuleInitializer]
            internal static void Init() { try { Console.OutputEncoding = new UTF8Encoding(false); } catch { } }
        }
        """;

    /// <summary>Исходник для показа ученику: без служебных global using и без хвоста.</summary>
    public static string DisplaySource(string source)
    {
        int cut = source.IndexOf(HiddenMarker, StringComparison.Ordinal);
        if (cut >= 0) source = source[..cut];
        var lines = source.Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.StartsWith("global using ", StringComparison.Ordinal));
        return string.Join("\n", lines).Trim();
    }

    public static string NormalizeSource(string code)
    {
        var parsed = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
        var root = (CompilationUnitSyntax)parsed.GetRoot();

        var usings = new List<UsingDirectiveSyntax>();
        var statements = new List<StatementSyntax>();
        var members = new List<MemberDeclarationSyntax>();

        foreach (var u in root.Usings) usings.Add(u);

        foreach (var m in root.Members)
        {
            if (m is GlobalStatementSyntax gs) statements.Add(gs.Statement);
            else members.Add(m);
        }

        bool hasMain = code.Contains("static void Main") || code.Contains("static async Task Main")
                       || code.Contains("static Task Main") || code.Contains("static int Main");

        bool needsAsync = statements.Any(ContainsAwait);
        bool gui = DetectFramework(code) == DesktopFramework;

        var builder = new StringBuilder();
        builder.AppendLine(UsingsBlock);
        foreach (var u in usings) builder.AppendLine(u.ToFullString().Trim());

        if (hasMain)
        {
            foreach (var m in members) builder.AppendLine(m.ToFullString().Trim());
            foreach (var s in statements) builder.AppendLine(s.ToFullString().Trim());
            builder.AppendLine(HiddenInit);
            return builder.ToString();
        }

        string mainSignature = needsAsync ? "public static async Task Main()" : "public static void Main()";

        builder.AppendLine("public static class LearnedProgram");
        builder.AppendLine("{");
        if (gui) builder.AppendLine("    [STAThread]");
        builder.AppendLine("    " + mainSignature);
        builder.AppendLine("    {");
        if (statements.Count > 0)
        {
            foreach (var s in statements)
                builder.AppendLine("        " + s.ToFullString().Trim());
        }
        else
        {
            builder.AppendLine("        // В примере нет исполняемых операторов: объявления типов сами ничего не выполняют.");
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();

        foreach (var m in members) builder.AppendLine(m.ToFullString().Trim());
        builder.AppendLine(HiddenInit);

        return builder.ToString();
    }

    /// <summary>
    /// Пример «ждёт клавиатуру» нельзя ни проверить запуском, ни показать ученику: он просто
    /// висит. Поэтому ввод в примере заменяется на строку-заглушку, и программа продолжает
    /// работу. Ученик в расширенном режиме видит именно этот обработанный исходник.
    /// </summary>
    static string MakeInputless(string code)
    {
        if (code.Length == 0) return code;

        var parsed = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest));
        var rewriter = new InputRewriter();
        var root = rewriter.Visit(parsed.GetRoot());
        return root.ToFullString();
    }

    /// <summary>Заглушка вместо Console.ReadLine()/ReadKey(): строка, которую видно в выводе.</summary>
    static readonly string[] ReplacementLiterals = { "\"42\"", "\"abc\"", "\"5\"", "\"7\"" };

    sealed class InputRewriter : CSharpSyntaxRewriter
    {
        int _taken;

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (IsInputCall(node.Expression))
            {
                var literal = ReplacementLiterals[_taken % ReplacementLiterals.Length];
                _taken++;
                var replaced = (ExpressionSyntax)SyntaxFactory.ParseExpression(literal)
                    .WithTriviaFrom(node);
                return replaced;
            }

            return base.VisitInvocationExpression(node);
        }

        static bool IsInputCall(ExpressionSyntax expr)
        {
            if (expr is not MemberAccessExpressionSyntax ma) return false;

            string member = ma.Name.Identifier.ValueText;
            if (member is not ("ReadLine" or "ReadKey" or "Read")) return false;

            var target = ma.Expression.ToString();
            return target is "Console" or "System.Console" or "System.Console.In" or "Console.In";
        }
    }

    static bool ContainsAwait(SyntaxNode node) =>
        node.DescendantNodes().OfType<AwaitExpressionSyntax>().Any() ||
        node.DescendantNodes().OfType<YieldStatementSyntax>().Any();

    static int _slot;

    /// <summary>
    /// Каждой проверке примера нужен СВОЙ файл сборки: несколько примеров компилируются
    /// параллельно, а Roslyn не может перезаписать dll, которую держит уже запущенный
    /// дочерний процесс. Поэтому имя всегда уникальное (имя_счётчик).
    /// </summary>
    static string UniqueName(string name) => name + "_" + Interlocked.Increment(ref _slot).ToString("x");

    public static CompileResult Compile(string code, string workDir, string name = "lesson_demo")
    {
        Directory.CreateDirectory(workDir);
        string framework = DetectFramework(code);
        var source = NormalizeSource(code);

        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var options = new CSharpCompilationOptions(OutputKind.ConsoleApplication,
            optimizationLevel: OptimizationLevel.Debug, allowUnsafe: true);

        string dllPath = "";
        EmitResult? emit = null;

        for (int attempt = 0; attempt < 4 && emit is null; attempt++)
        {
            string unique = UniqueName(name);
            var compilation = CSharpCompilation.Create(unique, new[] { tree }, References(framework), options);
            dllPath = Path.Combine(workDir, unique + ".dll");

            try
            {
                emit = compilation.Emit(dllPath);
            }
            catch (IOException)
            {
                // файл сборки занят предыдущим запуском — пробуем в новом файле
                emit = null;
                TryDelete(dllPath);
                Thread.Sleep(60);
            }
        }

        if (emit is null)
            return new CompileResult(false, null, "не удалось записать файл сборки (файл занят другим запуском)", source, framework);

        if (!emit.Success)
        {
            var diag = string.Join("\n", emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"строка {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.Id} — {d.GetMessage()}"));
            TryDelete(dllPath);
            TryDelete(Path.ChangeExtension(dllPath, ".pdb"));
            return new CompileResult(false, null, diag, source, framework);
        }

        WriteRuntimeConfig(dllPath, framework);
        File.WriteAllText(Path.ChangeExtension(dllPath, ".cs"), source);

        var notes = string.Join("\n", emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning && d.Id != "CS8019" && d.Id != "CS8933")
            .GroupBy(d => d.Id).Select(g => g.First())
            .Take(3)
            .Select(d => $"предупреждение: {d.Id} — {d.GetMessage()}"));

        return new CompileResult(true, dllPath, notes, source, framework);
    }

    public static void WriteRuntimeConfig(string dllPath, string framework)
    {
        File.WriteAllText(Path.ChangeExtension(dllPath, ".runtimeconfig.json"), $$"""
        {
          "runtimeOptions": {
            "tfm": "net10.0",
            "rollForward": "LatestMinor",
            "framework": { "name": "{{framework}}", "version": "{{RuntimeVersion.Value}}" }
          }
        }
        """);
    }

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* не критично */ }
    }

    sealed class VersionComparer : IComparer<string?>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? a, string? b)
        {
            var va = Version.TryParse(a, out var x) ? x : new Version(0, 0);
            var vb = Version.TryParse(b, out var y) ? y : new Version(0, 0);
            return va.CompareTo(vb);
        }
    }
}
