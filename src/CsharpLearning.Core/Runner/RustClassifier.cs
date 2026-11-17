namespace CsharpLearning.Core.Runner;

/// <summary>
/// Раскраска Rust в тех же цветах, что и C# (<see cref="CodeClassifier"/>): ключевые слова синие, управляющие
/// (if/match/loop/return…) фиолетовые, типы бирюзовые, функции и макросы жёлтые, локальные переменные голубые.
/// Простой лексер без разбора грамматики: годится для обрывков кода из учебника и для того, что ученик печатает.
/// </summary>
public static class RustClassifier
{
    static readonly HashSet<string> Control = new()
    {
        "if", "else", "match", "for", "while", "loop", "break", "continue", "return", "in",
    };

    static readonly HashSet<string> Keywords = new()
    {
        "fn", "let", "mut", "const", "static", "struct", "enum", "impl", "trait", "pub", "use", "mod", "crate",
        "self", "super", "as", "where", "ref", "move", "dyn", "unsafe", "async", "await", "type", "extern",
        "true", "false", "box", "macro_rules", "union",
    };

    static readonly HashSet<string> Primitives = new()
    {
        "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize",
        "f32", "f64", "bool", "char", "str",
    };

    sealed record Tok(int Start, int Length, string Kind, string Text);

    public static List<CodeSpan> Classify(string code)
    {
        var spans = new List<CodeSpan>();
        if (string.IsNullOrEmpty(code)) return spans;

        var toks = Lex(code, spans);
        var locals = DeclaredLocals(toks);

        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (t.Kind != "id") continue;

            string s = t.Text;
            string prev = i > 0 ? toks[i - 1].Text : "";
            string next = i + 1 < toks.Count ? toks[i + 1].Text : "";
            string next2 = i + 2 < toks.Count ? toks[i + 2].Text : "";

            CodeKind kind;
            if (Control.Contains(s)) kind = CodeKind.Control;
            else if (Keywords.Contains(s)) kind = CodeKind.Keyword;
            else if (Primitives.Contains(s)) kind = CodeKind.Type;
            else if (s == "Self") kind = CodeKind.Type;
            else if (next == "!" && next2 != "=") kind = CodeKind.Method;                         // println!, vec!
            else if (prev == "fn") kind = CodeKind.Method;                                      // fn name
            else if (prev is "struct" or "enum" or "trait" or "type" or "union" || (prev == "impl" && char.IsUpper(s[0])))
                kind = CodeKind.Type;
            else if (next == "(" && !locals.Contains(s)) kind = CodeKind.Method;                // вызов функции или метода
            else if (next == "::" && char.IsUpper(s[0])) kind = CodeKind.Type;                  // Vec::new, String::from
            else if (next == "::" && prev != "::") kind = CodeKind.Plain;                       // имя модуля: std::...
            else if (locals.Contains(s)) kind = CodeKind.Local;
            else if (char.IsUpper(s[0])) kind = CodeKind.Type;                                  // String, Option, Person
            else kind = CodeKind.Plain;

            if (kind != CodeKind.Plain) spans.Add(new CodeSpan(t.Start, t.Length, kind));
        }

        spans.Sort((a, b) => a.Start.CompareTo(b.Start));
        return spans;
    }

    // ---------- лексер ----------

    static List<Tok> Lex(string c, List<CodeSpan> spans)
    {
        var toks = new List<Tok>();
        int i = 0, n = c.Length;

        while (i < n)
        {
            char ch = c[i];

            if (char.IsWhiteSpace(ch)) { i++; continue; }

            // комментарии
            if (ch == '/' && i + 1 < n && c[i + 1] == '/')
            {
                int e = c.IndexOf('\n', i);
                if (e < 0) e = n;
                spans.Add(new CodeSpan(i, e - i, CodeKind.Comment));
                i = e;
                continue;
            }
            if (ch == '/' && i + 1 < n && c[i + 1] == '*')
            {
                int depth = 1, j = i + 2;
                while (j < n && depth > 0)
                {
                    if (c[j] == '/' && j + 1 < n && c[j + 1] == '*') { depth++; j += 2; }
                    else if (c[j] == '*' && j + 1 < n && c[j + 1] == '/') { depth--; j += 2; }
                    else j++;
                }
                spans.Add(new CodeSpan(i, j - i, CodeKind.Comment));
                i = j;
                continue;
            }

            // строки: "…", b"…", r"…", r#"…"#, br"…"
            if (TryString(c, i, out int end))
            {
                spans.Add(new CodeSpan(i, end - i, CodeKind.String));
                i = end;
                continue;
            }

            // символ 'x' или время жизни 'a
            if (ch == '\'')
            {
                if (i + 2 < n && c[i + 1] == '\\')
                {
                    int j = i + 2;
                    while (j < n && c[j] != '\'' && c[j] != '\n') j++;
                    int e2 = Math.Min(n, j + 1);
                    spans.Add(new CodeSpan(i, e2 - i, CodeKind.String));
                    i = e2;
                    continue;
                }
                if (i + 2 < n && c[i + 2] == '\'')
                {
                    spans.Add(new CodeSpan(i, 3, CodeKind.String));
                    i += 3;
                    continue;
                }
                if (i + 1 < n && (char.IsLetter(c[i + 1]) || c[i + 1] == '_'))
                {
                    int j = i + 1;
                    while (j < n && (char.IsLetterOrDigit(c[j]) || c[j] == '_')) j++;
                    spans.Add(new CodeSpan(i, j - i, CodeKind.Type));     // 'a, 'static
                    i = j;
                    continue;
                }
            }

            // числа
            if (char.IsDigit(ch))
            {
                int j = i;
                if (ch == '0' && j + 1 < n && (c[j + 1] is 'x' or 'o' or 'b'))
                {
                    j += 2;
                    while (j < n && (Uri.IsHexDigit(c[j]) || c[j] == '_')) j++;
                }
                else
                {
                    while (j < n && (char.IsDigit(c[j]) || c[j] == '_')) j++;
                    if (j + 1 < n && c[j] == '.' && char.IsDigit(c[j + 1]))
                    {
                        j++;
                        while (j < n && (char.IsDigit(c[j]) || c[j] == '_')) j++;
                    }
                    if (j < n && (c[j] is 'e' or 'E') && j + 1 < n && (char.IsDigit(c[j + 1]) || c[j + 1] is '+' or '-'))
                    {
                        j += 2;
                        while (j < n && char.IsDigit(c[j])) j++;
                    }
                }
                // суффикс типа: 10u8, 2.5f32
                while (j < n && (char.IsLetterOrDigit(c[j]) || c[j] == '_')) j++;
                spans.Add(new CodeSpan(i, j - i, CodeKind.Number));
                i = j;
                continue;
            }

            // идентификаторы и ключевые слова (в том числе r#ident)
            if (char.IsLetter(ch) || ch == '_')
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(c[j]) || c[j] == '_')) j++;
                toks.Add(new Tok(i, j - i, "id", c.Substring(i, j - i)));
                i = j;
                continue;
            }

            // ::, ->, =>, ==, != и т. п. — склеиваем двухсимвольные знаки
            if (i + 1 < n && ((ch == ':' && c[i + 1] == ':') || (ch == '-' && c[i + 1] == '>') || (ch == '=' && c[i + 1] == '>')
                              || (ch == '=' && c[i + 1] == '=') || (ch == '!' && c[i + 1] == '=')))
            {
                toks.Add(new Tok(i, 2, "op", c.Substring(i, 2)));
                i += 2;
                continue;
            }

            toks.Add(new Tok(i, 1, "op", ch.ToString()));
            i++;
        }

        return toks;
    }

    static bool TryString(string c, int i, out int end)
    {
        end = i;
        int n = c.Length, j = i;

        if (c[j] == 'b' && j + 1 < n && (c[j + 1] == '"' || c[j + 1] == 'r')) j++;           // b"…", br"…"
        if (j < n && c[j] == 'r' && j + 1 < n && (c[j + 1] == '"' || c[j + 1] == '#'))
        {
            int k = j + 1, hashes = 0;
            while (k < n && c[k] == '#') { hashes++; k++; }
            if (k >= n || c[k] != '"') return false;
            string close = "\"" + new string('#', hashes);
            int e = c.IndexOf(close, k + 1, StringComparison.Ordinal);
            end = e < 0 ? n : e + close.Length;
            return true;
        }

        if (j < n && c[j] == '"')
        {
            int k = j + 1;
            while (k < n && c[k] != '"')
            {
                if (c[k] == '\\') k++;
                k++;
            }
            end = Math.Min(n, k + 1);
            return true;
        }

        return false;
    }

    // ---------- локальные переменные ----------

    static HashSet<string> DeclaredLocals(List<Tok> t)
    {
        var set = new HashSet<string>();

        for (int i = 0; i < t.Count; i++)
        {
            string s = t[i].Text;

            // let x, let mut x, let (a, b), let Some(x)
            if (s == "let" && t[i].Kind == "id")
            {
                int j = i + 1;
                int paren = 0;
                while (j < t.Count && t[j].Text != "=" && t[j].Text != ";" && t[j].Text != ":" && !(t[j].Text == "else" && paren == 0))
                {
                    if (t[j].Text is "(" or "[") paren++;
                    if (t[j].Text is ")" or "]") paren--;
                    if (t[j].Kind == "id" && IsBinding(t, j)) set.Add(t[j].Text);
                    j++;
                }
            }
            // for x in …, for (a, b) in …
            else if (s == "for" && t[i].Kind == "id")
            {
                for (int j = i + 1; j < t.Count && t[j].Text != "in" && t[j].Text != "{"; j++)
                    if (t[j].Kind == "id" && IsBinding(t, j)) set.Add(t[j].Text);
            }
            // fn f(a: T, b: &mut U)
            else if (s == "fn" && t[i].Kind == "id")
            {
                int j = i + 1;
                while (j < t.Count && t[j].Text != "(" && t[j].Text != "{" && t[j].Text != ";") j++;
                int depth = 0;
                for (; j < t.Count; j++)
                {
                    if (t[j].Text == "(") depth++;
                    else if (t[j].Text == ")") { depth--; if (depth == 0) break; }
                    else if (t[j].Kind == "id" && depth == 1 && j + 1 < t.Count && t[j + 1].Text == ":" && t[j - 1].Text is "(" or "," or "mut")
                        set.Add(t[j].Text);
                }
            }
            // замыкания |a, b|
            else if (s == "|" && i > 0 && (t[i - 1].Text is "=" or "(" or "," or "move" or ":" || t[i - 1].Kind == "op"))
            {
                int j = i + 1;
                while (j < t.Count && t[j].Text != "|")
                {
                    if (t[j].Kind == "id" && IsBinding(t, j)) set.Add(t[j].Text);
                    if (j - i > 12) break;
                    j++;
                }
            }
        }

        set.Remove("self");
        return set;
    }

    // идентификатор в шаблоне — имя переменной, если это не mut/ref и не имя типа/варианта (с большой буквы) и не вызов
    static bool IsBinding(List<Tok> t, int j)
    {
        string s = t[j].Text;
        if (s is "mut" or "ref" or "_" or "in" or "box") return false;
        if (Keywords.Contains(s) || Primitives.Contains(s) || Control.Contains(s)) return false;
        if (char.IsUpper(s[0])) return false;
        if (j + 1 < t.Count && t[j + 1].Text is "(" or "::" or "{") return false;
        if (j > 0 && t[j - 1].Text == "::") return false;
        if (j > 0 && t[j - 1].Text == ":" ) return false;      // тип после двоеточия
        return true;
    }
}
