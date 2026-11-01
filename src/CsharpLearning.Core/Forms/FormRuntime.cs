using System.Globalization;
using System.Text;

namespace CsharpLearning.Core.Forms;

/// <summary>
/// «Живое» окно без настоящего WinForms: хранит состояние элементов и выполняет обработчики.
/// Приложение рисует по этому состоянию макет окна, поэтому кнопки в нём реально работают.
/// </summary>
public sealed class FormRuntime
{
    readonly Dictionary<string, string> _text = new();
    readonly Dictionary<string, bool> _checked = new();
    readonly Dictionary<string, bool> _visible = new();
    readonly Dictionary<string, bool> _enabled = new();
    readonly Dictionary<string, List<string>> _items = new();
    readonly Dictionary<string, string> _picture = new();
    readonly Dictionary<string, int> _progress = new();
    readonly Dictionary<string, object> _vars = new();
    int _depth;

    public FormSpec Spec { get; }

    /// <summary>Изменился элемент (по имени) или всё окно ("*").</summary>
    public event Action<string>? Changed;
    public event Action<string>? MessageShown;

    public FormRuntime(FormSpec spec)
    {
        Spec = spec;
        Reset();
    }

    public string TextOf(string c) => _text.GetValueOrDefault(c, "");
    public bool CheckedOf(string c) => _checked.GetValueOrDefault(c);
    public bool VisibleOf(string c) => _visible.GetValueOrDefault(c, true);
    public bool EnabledOf(string c) => _enabled.GetValueOrDefault(c, true);
    public IReadOnlyList<string> ItemsOf(string c) => _items.TryGetValue(c, out var l) ? l : new List<string>();
    public string PictureOf(string c) => _picture.GetValueOrDefault(c, "");
    public int ProgressOf(string c) => _progress.GetValueOrDefault(c);
    public object? Var(string name) => _vars.GetValueOrDefault(name);

    public void Reset()
    {
        _text.Clear(); _checked.Clear(); _visible.Clear(); _enabled.Clear(); _items.Clear(); _picture.Clear(); _progress.Clear(); _vars.Clear();
        foreach (var c in Spec.Controls)
        {
            _text[c.Name] = c.Text;
            _checked[c.Name] = c.Checked;
            _visible[c.Name] = c.Visible;
            _enabled[c.Name] = true;
            _items[c.Name] = new List<string>(c.Items);
            _picture[c.Name] = c.Picture;
            _progress[c.Name] = c.Value;
        }
        foreach (var v in Spec.State)
            _vars[v.Name] = v.Type switch
            {
                "int" => int.TryParse(v.Initial, out var i) ? i : 0,
                "double" => double.TryParse(v.Initial, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0.0,
                _ => v.Initial,
            };
        Changed?.Invoke("*");
    }

    // ---------------- действия пользователя ----------------
    public void Click(string control) => Raise(control, "Click");

    public void UserTypes(string control, string text)
    {
        if (_text.GetValueOrDefault(control) == text) return;
        _text[control] = text;
        Raise(control, "TextChanged");
    }

    public void UserChecks(string control, bool value)
    {
        _checked[control] = value;
        Raise(control, "CheckedChanged");
    }

    void Raise(string control, string evt)
    {
        var c = Spec.Find(control);
        if (c is null || _depth > 20) return;
        foreach (var h in c.Handlers.Where(h => h.Event == evt))
        {
            _depth++;
            try { Exec(h.Actions, new Dictionary<string, object>()); }
            catch (Exception ex) { MessageShown?.Invoke("Необработанное исключение: " + ex.Message); }
            finally { _depth--; }
        }
    }

    // ---------------- выполнение ----------------
    void Exec(List<ActionSpec> actions, Dictionary<string, object> locals)
    {
        foreach (var a in actions)
        {
            switch (a.Kind)
            {
                case ActionKind.SetText:
                {
                    string value = Eval(a.Value, locals);
                    bool changed = _text.GetValueOrDefault(a.Target) != value;
                    _text[a.Target] = value;
                    Changed?.Invoke(a.Target);
                    if (changed) Raise(a.Target, "TextChanged");
                    break;
                }
                case ActionKind.SetVar:
                    _vars[a.Target] = Coerce(Eval(a.Value, locals), Spec.State.FirstOrDefault(s => s.Name == a.Target)?.Type ?? "string");
                    break;
                case ActionKind.Inc:
                {
                    int by = a.Value.Length == 0 ? 1 : int.Parse(Eval(a.Value, locals), CultureInfo.CurrentCulture);
                    _vars[a.Target] = Convert.ToInt32(_vars.GetValueOrDefault(a.Target) ?? 0) + by;
                    break;
                }
                case ActionKind.AddItem:
                    if (!_items.ContainsKey(a.Target)) _items[a.Target] = new();
                    _items[a.Target].Add(Eval(a.Value, locals));
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.ClearItems:
                    if (_items.ContainsKey(a.Target)) _items[a.Target].Clear();
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.SetVisible:
                    _visible[a.Target] = a.Value == "toggle" ? !VisibleOf(a.Target) : a.Value == "true";
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.SetEnabled:
                    _enabled[a.Target] = a.Value == "toggle" ? !EnabledOf(a.Target) : a.Value == "true";
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.SetPicture:
                    _picture[a.Target] = a.Value;
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.SetProgress:
                    _progress[a.Target] = Math.Clamp(int.Parse(Eval(a.Value, locals), CultureInfo.CurrentCulture), 0, 100);
                    Changed?.Invoke(a.Target);
                    break;
                case ActionKind.Message:
                    MessageShown?.Invoke(Eval(a.Value, locals));
                    break;
                case ActionKind.If:
                    Exec(Check(a.Condition, locals) ? a.Then : a.Else, locals);
                    break;
                case ActionKind.For:
                {
                    int from = int.Parse(Eval(a.From, locals), CultureInfo.CurrentCulture);
                    int to = int.Parse(Eval(a.To, locals), CultureInfo.CurrentCulture);
                    for (int i = from; i <= to; i++)
                    {
                        locals[a.Target] = i;
                        Exec(a.Body, locals);
                    }
                    locals.Remove(a.Target);
                    break;
                }
                case ActionKind.Try:
                    try { Exec(a.Body, locals); }
                    catch (Exception) { Exec(a.Catch, locals); }
                    break;
            }
        }
        Changed?.Invoke("*");
    }

    static object Coerce(string text, string type) => type switch
    {
        "int" => int.Parse(text, CultureInfo.CurrentCulture),
        "double" => double.Parse(text, CultureInfo.CurrentCulture),
        _ => text,
    };

    // ---------------- шаблоны и выражения ----------------
    public string Eval(string template, Dictionary<string, object> locals)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0) { sb.Append(template, i, template.Length - i); break; }
            sb.Append(template, i, open - i);
            int close = template.IndexOf('}', open);
            if (close < 0) { sb.Append(template, open, template.Length - open); break; }

            string token = template.Substring(open + 1, close - open - 1);
            sb.Append(token.StartsWith('=') ? Calc(token[1..].Trim(), locals) : Format(Operand(token, locals)));
            i = close + 1;
        }
        return sb.ToString();
    }

    static string Format(object v) => v switch
    {
        double d => d.ToString("G", CultureInfo.CurrentCulture),
        _ => Convert.ToString(v, CultureInfo.CurrentCulture) ?? "",
    };

    /// <summary>name, name:int, name:double, name:upper/lower/len, now:формат — значение операнда.</summary>
    object Operand(string token, Dictionary<string, object> locals)
    {
        token = token.Trim();
        if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var lit) && !token.Contains(':'))
            return token.Contains('.') ? lit : (object)(int)lit;

        string name = token, fmt = "";
        int colon = token.IndexOf(':');
        if (colon > 0) { name = token[..colon]; fmt = token[(colon + 1)..]; }

        if (name == "now") return DateTime.Now.ToString(fmt.Length > 0 ? fmt : "HH:mm:ss", CultureInfo.CurrentCulture);

        object value = locals.TryGetValue(name, out var lv) ? lv
            : _vars.TryGetValue(name, out var vv) ? vv
            : TextOf(name);

        switch (fmt)
        {
            case "int": return value is int ? value : int.Parse(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "", CultureInfo.CurrentCulture);
            case "double": return value is double ? value : double.Parse(Convert.ToString(value, CultureInfo.CurrentCulture) ?? "", CultureInfo.CurrentCulture);
            case "upper": return (Convert.ToString(value, CultureInfo.CurrentCulture) ?? "").ToUpper();
            case "lower": return (Convert.ToString(value, CultureInfo.CurrentCulture) ?? "").ToLower();
            case "len": return (Convert.ToString(value, CultureInfo.CurrentCulture) ?? "").Length;
            case "": return value;
            default: return value is IFormattable f ? f.ToString(fmt, CultureInfo.CurrentCulture) : value;
        }
    }

    string Calc(string expr, Dictionary<string, object> locals)
    {
        var parts = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new FormatException("Выражение «" + expr + "» должно быть вида «a + b».");
        var a = Operand(parts[0], locals);
        var b = Operand(parts[2], locals);
        string op = parts[1];

        if (a is int ia && b is int ib)
        {
            long r = op switch
            {
                "+" => (long)ia + ib,
                "-" => (long)ia - ib,
                "*" => (long)ia * ib,
                "/" => ib == 0 ? throw new DivideByZeroException("Деление на ноль") : ia / ib,
                "%" => ib == 0 ? throw new DivideByZeroException("Деление на ноль") : ia % ib,
                _ => throw new FormatException("Неизвестная операция " + op),
            };
            return r.ToString(CultureInfo.CurrentCulture);
        }

        double da = Convert.ToDouble(a, CultureInfo.CurrentCulture), db = Convert.ToDouble(b, CultureInfo.CurrentCulture);
        double rd = op switch { "+" => da + db, "-" => da - db, "*" => da * db, "/" => da / db, "%" => da % db, _ => throw new FormatException("Неизвестная операция " + op) };
        return rd.ToString("G", CultureInfo.CurrentCulture);
    }

    // ---------------- условия ----------------
    bool Check(string cond, Dictionary<string, object> locals)
    {
        cond = cond.Trim();
        bool neg = cond.StartsWith('!');
        if (neg) cond = cond[1..];

        bool result;
        if (cond.EndsWith(".IsEmpty")) result = string.IsNullOrWhiteSpace(TextOf(cond[..^8]));
        else if (cond.EndsWith(".Checked")) result = CheckedOf(cond[..^8]);
        else if (cond.EndsWith(".NotInt")) result = !int.TryParse(TextOf(cond[..^7]), NumberStyles.Integer, CultureInfo.CurrentCulture, out _);
        else
        {
            string[] ops = { "==", "!=", "<=", ">=", "<", ">" };
            string? op = ops.FirstOrDefault(o => cond.Contains(" " + o + " "));
            if (op is null) throw new FormatException("Не понимаю условие «" + cond + "»");
            var sides = cond.Split(" " + op + " ", 2);
            object Side(string t) => t.Trim().StartsWith('"') ? t.Trim().Trim('"') : Operand(t, locals);
            var l = Side(sides[0]);
            var r = Side(sides[1]);
            int cmp = l is string || r is string
                ? string.Compare(Format(l), Format(r), StringComparison.Ordinal)
                : Convert.ToDouble(l, CultureInfo.CurrentCulture).CompareTo(Convert.ToDouble(r, CultureInfo.CurrentCulture));
            result = op switch { "==" => cmp == 0, "!=" => cmp != 0, "<" => cmp < 0, ">" => cmp > 0, "<=" => cmp <= 0, _ => cmp >= 0 };
        }
        return neg ? !result : result;
    }
}
