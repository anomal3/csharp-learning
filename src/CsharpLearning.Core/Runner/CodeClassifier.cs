using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CsharpLearning.Core.Runner;

public enum CodeKind { Plain, Keyword, Control, Type, Method, Local, String, Number, Comment, Operator }

public readonly record struct CodeSpan(int Start, int Length, CodeKind Kind);

/// <summary>
/// Раскраска C# как в Visual Studio (тёмная тема): ключевые слова синие, управляющие (if/for/return…)
/// фиолетовые, типы бирюзовые, методы жёлтые, локальные переменные голубые, строки, числа, комментарии.
/// Работает по синтаксическому дереву Roslyn без семантики — поэтому годится для обрывков кода
/// из учебника и для текста, который ученик печатает прямо сейчас.
/// </summary>
public static class CodeClassifier
{
    static readonly HashSet<SyntaxKind> ControlKeywords = new()
    {
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.ForKeyword, SyntaxKind.ForEachKeyword, SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
        SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword, SyntaxKind.ReturnKeyword, SyntaxKind.GotoKeyword,
        SyntaxKind.ThrowKeyword, SyntaxKind.TryKeyword, SyntaxKind.CatchKeyword, SyntaxKind.FinallyKeyword,
        SyntaxKind.YieldKeyword, SyntaxKind.AwaitKeyword,
    };

    public static List<CodeSpan> Classify(string code)
    {
        var spans = new List<CodeSpan>();
        if (string.IsNullOrEmpty(code)) return spans;

        SyntaxNode root;
        try
        {
            root = CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(LanguageVersion.Latest)).GetRoot();
        }
        catch
        {
            return spans;
        }

        var locals = DeclaredLocals(root);
        var types = DeclaredTypes(root);

        foreach (var token in root.DescendantTokens())
        {
            AddComments(spans, token.LeadingTrivia);

            var kind = Of(token, locals, types);
            if (kind != CodeKind.Plain && token.Span.Length > 0)
                spans.Add(new CodeSpan(token.SpanStart, token.Span.Length, kind));

            AddComments(spans, token.TrailingTrivia);
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    static void AddComments(List<CodeSpan> spans, SyntaxTriviaList trivia)
    {
        foreach (var t in trivia)
            if (t.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia)
                spans.Add(new CodeSpan(t.SpanStart, t.Span.Length, CodeKind.Comment));
    }

    static HashSet<string> DeclaredLocals(SyntaxNode root)
    {
        var set = new HashSet<string>();
        foreach (var n in root.DescendantNodes())
        {
            switch (n)
            {
                case VariableDeclaratorSyntax { Parent.Parent: not (FieldDeclarationSyntax or EventFieldDeclarationSyntax) } v:
                    set.Add(v.Identifier.Text); break;
                case ParameterSyntax p: set.Add(p.Identifier.Text); break;
                case ForEachStatementSyntax f: set.Add(f.Identifier.Text); break;
                case CatchDeclarationSyntax c when c.Identifier.Text.Length > 0: set.Add(c.Identifier.Text); break;
                case SingleVariableDesignationSyntax s: set.Add(s.Identifier.Text); break;
            }
        }
        return set;
    }

    static HashSet<string> DeclaredTypes(SyntaxNode root)
    {
        var set = new HashSet<string>();
        foreach (var n in root.DescendantNodes())
            if (n is BaseTypeDeclarationSyntax b) set.Add(b.Identifier.Text);
            else if (n is DelegateDeclarationSyntax d) set.Add(d.Identifier.Text);
            else if (n is TypeParameterSyntax tp) set.Add(tp.Identifier.Text);
        return set;
    }

    static CodeKind Of(SyntaxToken token, HashSet<string> locals, HashSet<string> types)
    {
        var k = token.Kind();

        if (k is SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken
            or SyntaxKind.InterpolatedVerbatimStringStartToken or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8StringLiteralToken
            or SyntaxKind.InterpolatedSingleLineRawStringStartToken or SyntaxKind.InterpolatedRawStringEndToken)
            return CodeKind.String;

        if (k == SyntaxKind.NumericLiteralToken) return CodeKind.Number;

        if (ControlKeywords.Contains(k)) return CodeKind.Control;
        if (k == SyntaxKind.DefaultKeyword && token.Parent is DefaultSwitchLabelSyntax) return CodeKind.Control;

        if (SyntaxFacts.IsKeywordKind(k) || (SyntaxFacts.IsContextualKeyword(k) && k != SyntaxKind.IdentifierToken))
            return CodeKind.Keyword;

        if (k != SyntaxKind.IdentifierToken) return CodeKind.Plain;

        string text = token.ValueText;
        var node = token.Parent;

        // var — «умное» слово, в дереве это обычное имя в позиции типа
        if (text == "var" && node is IdentifierNameSyntax { Parent: VariableDeclarationSyntax or ForEachStatementSyntax or DeclarationPatternSyntax or DeclarationExpressionSyntax })
            return CodeKind.Keyword;

        switch (node)
        {
            case BaseTypeDeclarationSyntax b when b.Identifier == token: return CodeKind.Type;
            case DelegateDeclarationSyntax d when d.Identifier == token: return CodeKind.Type;
            case TypeParameterSyntax: return CodeKind.Type;
            case MethodDeclarationSyntax m when m.Identifier == token: return CodeKind.Method;
            case LocalFunctionStatementSyntax lf when lf.Identifier == token: return CodeKind.Method;
            case ConstructorDeclarationSyntax or DestructorDeclarationSyntax: return CodeKind.Type;
            case VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax }: return CodeKind.Plain;
            case VariableDeclaratorSyntax: return CodeKind.Local;
            case ParameterSyntax: return CodeKind.Local;
            case ForEachStatementSyntax: return CodeKind.Local;
            case SingleVariableDesignationSyntax: return CodeKind.Local;
            case CatchDeclarationSyntax: return CodeKind.Local;
        }

        if (node is GenericNameSyntax gn)
            return IsInvoked(gn) ? CodeKind.Method : CodeKind.Type;

        if (node is IdentifierNameSyntax name)
        {
            // вызов метода: Foo(...) или x.Foo(...)
            if (IsInvoked(name)) return CodeKind.Method;

            if (InTypePosition(name)) return types.Contains(text) || !locals.Contains(text) ? CodeKind.Type : CodeKind.Local;

            if (name.Parent is MemberAccessExpressionSyntax ma)
            {
                if (ma.Name == name) return CodeKind.Plain;             // свойство или поле
                // левая часть «A.B»: объявленная переменная — локальная, иначе это имя типа (Console, Math…)
                if (locals.Contains(text)) return CodeKind.Local;
                if (types.Contains(text) || char.IsUpper(text[0])) return CodeKind.Type;
                return CodeKind.Plain;
            }

            if (locals.Contains(text)) return CodeKind.Local;
            if (types.Contains(text)) return CodeKind.Type;
        }

        return CodeKind.Plain;
    }

    static bool IsInvoked(SimpleNameSyntax name) => name.Parent switch
    {
        InvocationExpressionSyntax inv => inv.Expression == name,
        MemberAccessExpressionSyntax ma when ma.Name == name => ma.Parent is InvocationExpressionSyntax inv2 && inv2.Expression == ma,
        MemberBindingExpressionSyntax mb => mb.Parent is InvocationExpressionSyntax inv3 && inv3.Expression == mb,
        _ => false,
    };

    static bool InTypePosition(IdentifierNameSyntax name) => name.Parent switch
    {
        VariableDeclarationSyntax v => v.Type == name,
        ParameterSyntax p => p.Type == name,
        ObjectCreationExpressionSyntax o => o.Type == name,
        CastExpressionSyntax c => c.Type == name,
        TypeArgumentListSyntax => true,
        ArrayTypeSyntax => true,
        NullableTypeSyntax => true,
        SimpleBaseTypeSyntax => true,
        TypeOfExpressionSyntax => true,
        DeclarationPatternSyntax => true,
        DeclarationExpressionSyntax => true,
        ForEachStatementSyntax f => f.Type == name,
        MethodDeclarationSyntax m => m.ReturnType == name,
        PropertyDeclarationSyntax p => p.Type == name,
        IndexerDeclarationSyntax i => i.Type == name,
        QualifiedNameSyntax => true,
        BinaryExpressionSyntax { RawKind: (int)SyntaxKind.IsExpression or (int)SyntaxKind.AsExpression } b => b.Right == name,
        TypeConstraintSyntax => true,
        _ => false,
    };
}
