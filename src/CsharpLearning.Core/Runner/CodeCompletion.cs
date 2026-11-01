using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsharpLearning.Core.Runner;

public sealed record CompletionItem(string Text, string Hint);

/// <summary>
/// Мини-IntelliSense на самом Roslyn: пример урока компилируется один раз, дальше по набранному
/// началу слова (или по «Имя.») отдаются типы, переменные и члены. Работает без SDK:
/// reference-сборки лежат рядом с программой (см. IlWorkbench).
/// </summary>
public static class CodeCompletion
{
    sealed record Ctx(SemanticModel Model, int Pos);

    static readonly ConcurrentDictionary<string, Lazy<Ctx?>> Cache = new();
    static readonly ConcurrentDictionary<string, MetadataReference[]> RefCache = new();

    static readonly string[] Keywords =
    {
        "abstract", "async", "await", "bool", "break", "byte", "case", "catch", "char", "class", "const",
        "continue", "decimal", "default", "do", "double", "else", "enum", "false", "finally", "float", "for",
        "foreach", "if", "in", "int", "interface", "is", "long", "namespace", "new", "null", "object",
        "out", "override", "private", "public", "readonly", "ref", "return", "static", "string", "struct",
        "switch", "this", "throw", "true", "try", "typeof", "using", "var", "virtual", "void", "while",
    };

    static readonly Regex MemberAccess = new(@"(?<chain>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\.(?<prefix>\w*)$", RegexOptions.Compiled);
    static readonly Regex PlainWord = new(@"(?<![\w.])(?<prefix>[A-Za-z_]\w*)$", RegexOptions.Compiled);

    /// <summary>Прогрев: собрать компиляцию заранее, чтобы первая подсказка не тормозила.</summary>
    public static void Warm(string code)
    {
        if (!string.IsNullOrWhiteSpace(code)) _ = Task.Run(() => Get(code));
    }

    static Ctx? Get(string code) => Cache.GetOrAdd(code, c => new Lazy<Ctx?>(() => Build(c))).Value;

    static Ctx? Build(string code)
    {
        try
        {
            string framework = IlWorkbench.DetectFramework(code);
            var refs = RefCache.GetOrAdd(framework, f => IlWorkbench.References(f).ToArray());
            if (refs.Length == 0) return null;

            var source = IlWorkbench.NormalizeSource(code);
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
            var compilation = CSharpCompilation.Create("completion_" + Guid.NewGuid().ToString("N"),
                new[] { tree }, refs, new CSharpCompilationOptions(OutputKind.ConsoleApplication, allowUnsafe: true));
            var model = compilation.GetSemanticModel(tree);

            var root = tree.GetRoot();
            var main = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.Identifier.Text == "Main");
            // позиция «после последнего оператора Main»: здесь видны все объявленные в Main переменные
            int pos = main?.Body is { } body
                ? (body.Statements.Count > 0 ? body.Statements.Last().Span.End : body.OpenBraceToken.Span.End)
                : root.FullSpan.End;
            return new Ctx(model, pos);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Подсказки для текста слева от каретки на текущей строке.</summary>
    public static Task<List<CompletionItem>> GetAsync(string code, string textBeforeCaret) =>
        Task.Run(() => Query(code, textBeforeCaret));

    static List<CompletionItem> Query(string code, string before)
    {
        var result = new List<CompletionItem>();
        try
        {
            var ctx = Get(code);
            if (ctx is null) return PlainKeywords(before);

            var member = MemberAccess.Match(before);
            if (member.Success) return Members(ctx, member.Groups["chain"].Value, member.Groups["prefix"].Value);

            var plain = PlainWord.Match(before);
            if (plain.Success) return Scope(ctx, plain.Groups["prefix"].Value);
        }
        catch { /* подсказка — удобство, не повод падать */ }
        return result;
    }

    static List<CompletionItem> PlainKeywords(string before)
    {
        var plain = PlainWord.Match(before);
        if (!plain.Success) return new();
        string p = plain.Groups["prefix"].Value;
        return Keywords.Where(k => k.StartsWith(p, StringComparison.OrdinalIgnoreCase) && k != p)
            .Select(k => new CompletionItem(k, "ключевое слово")).Take(10).ToList();
    }

    static List<CompletionItem> Scope(Ctx ctx, string prefix)
    {
        var symbols = ctx.Model.LookupSymbols(ctx.Pos)
            .Where(s => Visible(s) && s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && s.Name != prefix)
            .GroupBy(s => s.Name)
            .Select(g => (name: g.Key, sym: g.OrderBy(Rank).First()))
            .OrderBy(x => Rank(x.sym)).ThenBy(x => x.name.Length).ThenBy(x => x.name, StringComparer.Ordinal)
            .Select(x => new CompletionItem(x.name, Describe(x.sym)))
            .ToList();

        symbols.AddRange(Keywords.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k != prefix)
            .Select(k => new CompletionItem(k, "ключевое слово")));
        return symbols.Take(12).ToList();
    }

    static List<CompletionItem> Members(Ctx ctx, string chain, string prefix)
    {
        ISymbol? cur = null;
        INamespaceOrTypeSymbol? container = null;

        foreach (var part in chain.Split('.'))
        {
            var found = container is null
                ? ctx.Model.LookupSymbols(ctx.Pos, name: part)
                : ctx.Model.LookupSymbols(ctx.Pos, container, part);
            cur = found.FirstOrDefault(s => s is not IMethodSymbol { MethodKind: not MethodKind.Ordinary })
                  ?? found.FirstOrDefault();
            if (cur is null) return new();

            container = cur switch
            {
                INamespaceOrTypeSymbol t => t,
                ILocalSymbol l => l.Type,
                IParameterSymbol p => p.Type,
                IFieldSymbol f => f.Type,
                IPropertySymbol pr => pr.Type,
                IMethodSymbol m => m.ReturnType,
                _ => null,
            };
            if (container is null) return new();
        }

        bool viaType = cur is INamedTypeSymbol or INamespaceSymbol;

        return ctx.Model.LookupSymbols(ctx.Pos, container, name: null, includeReducedExtensionMethods: !viaType)
            .Where(s => Visible(s) && (viaType ? (s.IsStatic || s is INamedTypeSymbol or INamespaceSymbol) : !s.IsStatic || s is IMethodSymbol { IsExtensionMethod: true })
                        && s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        && (s is not IMethodSymbol ms || ms.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension))
            .GroupBy(s => s.Name)
            .Select(g => g.OrderBy(Rank).First())
            .OrderBy(Rank).ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new CompletionItem(s.Name, Describe(s)))
            .Take(14)
            .ToList();
    }

    static bool Visible(ISymbol s) =>
        !s.IsImplicitlyDeclared && s.Name.Length > 0 && !s.Name.StartsWith('<') && !s.Name.StartsWith('.') &&
        s.DeclaredAccessibility is Accessibility.Public or Accessibility.NotApplicable or Accessibility.Internal
            or Accessibility.Protected or Accessibility.ProtectedOrInternal;

    static int Rank(ISymbol s) => s switch
    {
        ILocalSymbol or IParameterSymbol => 0,
        IFieldSymbol or IPropertySymbol => 1,
        IMethodSymbol => 2,
        INamedTypeSymbol => 3,
        INamespaceSymbol => 4,
        _ => 5,
    };

    static string Describe(ISymbol s) => s switch
    {
        ILocalSymbol l => "переменная · " + l.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IParameterSymbol p => "параметр · " + p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IFieldSymbol f => "поле · " + f.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IPropertySymbol p => "свойство · " + p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        IMethodSymbol m => "метод · " + m.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
        INamedTypeSymbol t => t.TypeKind switch
        {
            TypeKind.Interface => "интерфейс",
            TypeKind.Struct => "структура",
            TypeKind.Enum => "перечисление",
            TypeKind.Delegate => "делегат",
            _ => "класс",
        },
        INamespaceSymbol => "пространство имён",
        IEventSymbol => "событие",
        _ => "",
    };
}
