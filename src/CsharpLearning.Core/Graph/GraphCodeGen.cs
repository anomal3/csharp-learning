using System.Text;

namespace CsharpLearning.Core.Graph;

public sealed record GraphIssue(bool IsError, string Class, string Method, string NodeId, string Message);

public sealed record GenResult(string Code, List<GraphIssue> Issues, Dictionary<string, (int from, int to)> NodeLines)
{
    public bool HasErrors => Issues.Any(i => i.IsError);
}

public sealed record ScopeVar(string Name, string Type, string Origin);

/// <summary>Какие переменные и поля видны внутри метода (для типов пинов и палитры).</summary>
public static class GraphScope
{
    public static List<ScopeVar> Vars(GraphProject p, ClassDef cls, MethodDef m)
    {
        var l = new List<ScopeVar>();
        foreach (var prm in m.Params) l.Add(new(prm.Name, prm.Type, "параметр"));
        foreach (var loc in m.Locals) l.Add(new(loc.Name, loc.Type, "переменная"));

        foreach (var n in m.Graph.Nodes)
        {
            var c = new NodeCtx(n, p, cls, m);
            switch (n.Kind)
            {
                case "DeclareVar": l.Add(new(GraphTypes.Ident(c.Prop("Name")), c.Prop("Type"), "переменная")); break;
                case "ForRange": l.Add(new(GraphTypes.Ident(c.Prop("Var"), "i"), "int", "счётчик")); break;
                case "ForEach": l.Add(new(GraphTypes.Ident(c.Prop("Var"), "item"), c.Prop("T"), "элемент")); break;
            }
        }

        for (var c2 = cls; c2 is not null;)
        {
            foreach (var f in c2.Fields) l.Add(new(f.Name, f.Type, "поле"));
            c2 = p.FindClass(c2.BaseClass);
            if (l.Count > 500) break;
        }
        return l;
    }

    public static string? TypeOf(GraphProject p, ClassDef cls, MethodDef m, string name) =>
        string.IsNullOrEmpty(name) ? null : Vars(p, cls, m).FirstOrDefault(v => v.Name == name)?.Type;

    public static FieldDef? FindField(GraphProject p, string? className, string? field)
    {
        for (var c = p.FindClass(className); c is not null; c = p.FindClass(c.BaseClass))
        {
            var f = c.FindField(field);
            if (f is not null) return f;
            if (c.BaseClass is null) break;
        }
        return null;
    }

    public static MethodDef? FindMethod(GraphProject p, string? className, string? method)
    {
        for (var c = p.FindClass(className); c is not null; c = p.FindClass(c.BaseClass))
        {
            var m = c.FindMethod(method);
            if (m is not null) return m;
            if (c.BaseClass is null) break;
        }
        return null;
    }

    /// <summary>Убирает провода, которым больше некуда подключаться (сменили тип, метод, класс, параметры…).</summary>
    public static int PruneWires(GraphProject p, ClassDef cls, MethodDef m)
    {
        var cache = new Dictionary<string, List<Pin>>();
        List<Pin> PinsOf(string nodeId)
        {
            if (cache.TryGetValue(nodeId, out var l)) return l;
            var n = m.Graph.Find(nodeId);
            return cache[nodeId] = n is null ? new() : GraphScope.PinsOf(p, cls, m, n);
        }

        return m.Graph.Wires.RemoveAll(w =>
        {
            var from = PinsOf(w.FromNode).FirstOrDefault(x => x.IsOutput && x.Id == w.FromPin);
            var to = PinsOf(w.ToNode).FirstOrDefault(x => !x.IsOutput && x.Id == w.ToPin);
            if (from is null || to is null) return true;
            if (from.IsExec != to.IsExec) return true;
            return !from.IsExec && !GraphTypes.CanAssign(p, from.Type, to.Type);
        });
    }

    public static void PruneAll(GraphProject p)
    {
        foreach (var c in p.Classes)
            foreach (var m in c.Methods)
                PruneWires(p, c, m);
    }

    /// <summary>Пины ноды в данном методе (с учётом её настроек).</summary>
    public static List<Pin> PinsOf(GraphProject p, ClassDef cls, MethodDef m, NodeData n)
    {
        var spec = NodeSpecs.Get(n.Kind);
        return spec?.Pins(new NodeCtx(n, p, cls, m)) ?? new List<Pin>();
    }
}

/// <summary>
/// Превращает граф в обычный исходный код C#. Алгоритм:
/// 1) класс → поля и методы; 2) тело метода — цепочка Exec-нод от «Начала»;
/// 3) Pure-ноды (формулы) подставляются выражениями прямо в место использования;
/// 4) результат Exec-ноды (вызов метода, ReadLine…) сохраняется во временную переменную;
/// 5) ветвления и циклы пишут вложенные блоки { } по своим выходам потока.
/// </summary>
public sealed class GraphCodeGen
{
    readonly GraphProject _p;
    readonly StringBuilder _sb = new();
    int _indent;
    int _lines;
    ClassDef _cls = null!;
    MethodDef _m = null!;
    GraphData _g = null!;
    int _temp;
    int _pureDepth;
    readonly Dictionary<string, string> _outs = new();
    readonly HashSet<string> _emitted = new();
    readonly HashSet<string> _path = new();

    public List<GraphIssue> Issues { get; } = new();
    public Dictionary<string, (int from, int to)> NodeLines { get; } = new();

    GraphCodeGen(GraphProject p) => _p = p;

    public static GenResult Generate(GraphProject p, bool withUsings = true)
    {
        var gen = new GraphCodeGen(p);
        gen.Run(withUsings);
        return new GenResult(gen._sb.ToString().TrimEnd() + "\n", gen.Issues, gen.NodeLines);
    }

    // ---------------- сборка файла ----------------
    void Run(bool withUsings)
    {
        if (withUsings)
        {
            _sb.AppendLine("using System;");
            _sb.AppendLine("using System.Collections.Generic;");
            _sb.AppendLine("using System.Linq;");
            _sb.AppendLine();
            _lines += 4;
        }

        var entry = _p.FindClass(_p.EntryClass)?.FindMethod(_p.EntryMethod);
        if (entry is null)
            Issues.Add(new(true, _p.EntryClass, _p.EntryMethod, "", $"Нет точки входа: нужен метод {_p.EntryClass}.{_p.EntryMethod}."));
        else if (!entry.IsStatic || entry.Params.Count > 0)
            Issues.Add(new(true, _p.EntryClass, _p.EntryMethod, "", "Точка входа должна быть static и без параметров."));

        var names = new HashSet<string>();
        foreach (var c in _p.Classes)
        {
            if (!names.Add(c.Name)) Issues.Add(new(true, c.Name, "", "", $"Класс «{c.Name}» объявлен дважды."));
            EmitClass(c);
            Raw("");
        }
    }

    void Raw(string s)
    {
        _sb.AppendLine(s);
        _lines++;
    }

    public void Line(string s) => Raw(new string(' ', _indent * 4) + s);

    void EmitClass(ClassDef c)
    {
        _cls = c;
        string head = "public " + (c.IsStatic ? "static " : "") + "class " + GraphTypes.Ident(c.Name, "Класс");
        if (!string.IsNullOrEmpty(c.BaseClass) && !c.IsStatic) head += " : " + GraphTypes.Ident(c.BaseClass);
        Line(head);
        Line("{");
        _indent++;

        foreach (var f in c.Fields)
        {
            string st = (c.IsStatic || f.IsStatic) ? "static " : "";
            string init = string.IsNullOrWhiteSpace(f.Initial) ? "" : " = " + f.Initial;
            Line(f.IsProperty
                ? $"public {st}{f.Type} {GraphTypes.Ident(f.Name)} {{ get; set; }}{init}"
                : $"public {st}{f.Type} {GraphTypes.Ident(f.Name)}{init};");
        }
        if (c.Fields.Count > 0 && c.Methods.Count > 0) Raw("");

        for (int i = 0; i < c.Methods.Count; i++)
        {
            EmitMethod(c, c.Methods[i]);
            if (i < c.Methods.Count - 1) Raw("");
        }

        _indent--;
        Line("}");
    }

    void EmitMethod(ClassDef c, MethodDef m)
    {
        _cls = c; _m = m; _g = m.Graph;
        _temp = 0; _pureDepth = 0;
        _outs.Clear(); _emitted.Clear(); _path.Clear();

        string prms = string.Join(", ", m.Params.Select(p => $"{p.Type} {GraphTypes.Ident(p.Name)}"));
        bool isStatic = c.IsStatic || m.IsStatic;
        string sig;
        if (m.IsCtor) sig = $"public {GraphTypes.Ident(c.Name)}({prms})";
        else
        {
            string mod = m.Modifier is "virtual" or "override" && !isStatic ? m.Modifier + " " : "";
            sig = $"public {(isStatic ? "static " : "")}{mod}{(m.IsVoid ? "void" : m.ReturnType)} {GraphTypes.Ident(m.Name)}({prms})";
        }

        Line(sig);
        Line("{");
        _indent++;

        // локальные переменные объявляются сразу в начале метода — их видно из любой ветки графа
        foreach (var loc in m.Locals)
        {
            string init = string.IsNullOrWhiteSpace(loc.Initial) ? GraphTypes.DefaultExpr(loc.Type) : GraphTypes.Literal(loc.Type, loc.Initial);
            Line($"{loc.Type} {GraphTypes.Ident(loc.Name)} = {init};");
        }

        var entry = _g.Entry;
        if (entry is null) Issues.Add(new(true, c.Name, m.Name, "", "В методе нет ноды «Начало»."));
        else Chain(Target(entry.Id, "then"));

        _indent--;
        Line("}");

        // недостижимые ноды — подсказка ученику
        foreach (var n in _g.Nodes)
        {
            var spec = NodeSpecs.Get(n.Kind);
            if (spec is { Mode: NodeMode.Exec } && !_emitted.Contains(n.Id))
                Issues.Add(new(false, c.Name, m.Name, n.Id, $"Нода «{spec.Title(new NodeCtx(n, _p, c, m))}» не подключена к потоку — в код не попала."));
        }
    }

    // ---------------- обход потока ----------------
    NodeData? Target(string nodeId, string pin)
    {
        var w = _g.Wires.FirstOrDefault(x => x.FromNode == nodeId && x.FromPin == pin);
        return w is null ? null : _g.Find(w.ToNode);
    }

    public bool HasTarget(NodeCtx c, string execPin) => Target(c.Node.Id, execPin) is not null;

    public bool HasWire(NodeCtx c, string pin) => _g.Wires.Any(w => w.ToNode == c.Node.Id && w.ToPin == pin);

    void Chain(NodeData? start)
    {
        var added = new List<string>();
        var n = start;

        while (n is not null)
        {
            var spec = NodeSpecs.Get(n.Kind);
            var ctx = new NodeCtx(n, _p, _cls, _m);

            if (spec?.Emit is null)
            {
                Issues.Add(new(true, _cls.Name, _m.Name, n.Id, $"Нода «{n.Kind}» не умеет быть шагом программы."));
                break;
            }
            if (!_path.Add(n.Id))
            {
                Issues.Add(new(true, _cls.Name, _m.Name, n.Id, "Поток замкнулся сам на себя. Для повторов используйте цикл (for / while)."));
                break;
            }
            added.Add(n.Id);

            if (!_emitted.Add(n.Id))
                Issues.Add(new(false, _cls.Name, _m.Name, n.Id, $"Нода «{spec.Title(ctx)}» достижима двумя путями — её код продублирован."));

            int from = _lines + 1;
            string? next = spec.Emit(this, ctx);
            NodeLines[$"{_cls.Name}.{_m.Name}:{n.Id}"] = (from, _lines);

            n = next is null ? null : Target(n.Id, next);
        }

        foreach (var id in added) _path.Remove(id);
    }

    public void Block(NodeCtx c, string execPin)
    {
        Line("{");
        _indent++;
        Chain(Target(c.Node.Id, execPin));
        _indent--;
        Line("}");
    }

    // ---------------- значения ----------------
    public string NewTemp(string prefix)
    {
        string p = GraphTypes.Ident(prefix, "v");
        return $"{p}{++_temp}";
    }

    public void SetOut(NodeCtx c, string pin, string expr) => _outs[c.Node.Id + ":" + pin] = expr;

    public void Error(NodeCtx c, string msg) => Issues.Add(new(true, _cls.Name, _m.Name, c.Node.Id, msg));

    static string Coerce(string expr, string from, string to)
    {
        if (from == to || string.IsNullOrEmpty(to) || to == "object") return expr;
        if (to == "string") return from == "string" ? expr : $"Convert.ToString({expr})";
        return expr;
    }

    /// <summary>Выражение для входного пина: либо то, что подключено проводом, либо введённое прямо в ноде значение.</summary>
    public string In(NodeCtx c, string pinId)
    {
        var pins = NodeSpecs.Get(c.Node.Kind)?.Pins(c) ?? new();
        var pin = pins.FirstOrDefault(p => p.Id == pinId && !p.IsOutput);
        if (pin is null) return "default";

        var wire = _g.Wires.FirstOrDefault(w => w.ToNode == c.Node.Id && w.ToPin == pinId);
        if (wire is not null)
        {
            var src = _g.Find(wire.FromNode);
            if (src is not null)
            {
                var srcCtx = new NodeCtx(src, _p, _cls, _m);
                var srcPin = (NodeSpecs.Get(src.Kind)?.Pins(srcCtx) ?? new()).FirstOrDefault(p => p.Id == wire.FromPin && p.IsOutput);
                string expr = Out(srcCtx, wire.FromPin);
                return Coerce(expr, srcPin?.Type ?? "", pin.Type);
            }
        }

        string text = c.Node.Props.TryGetValue("in:" + pinId, out var v) ? v : pin.Default;
        return GraphTypes.Literal(pin.Type, text);
    }

    /// <summary>То же, что In, но без лишних внешних скобок — для мест, где скобки не нужны (аргументы, условия, присваивания).</summary>
    public string InTop(NodeCtx c, string pinId) => StripParens(In(c, pinId));

    static string StripParens(string e)
    {
        while (e.Length > 2 && e[0] == '(' && e[^1] == ')')
        {
            int depth = 0; bool wraps = true;
            for (int i = 0; i < e.Length - 1; i++)
            {
                if (e[i] == '"') { i = SkipString(e, i); continue; }
                if (e[i] == '(') depth++;
                else if (e[i] == ')' && --depth == 0) { wraps = false; break; }
            }
            if (!wraps) break;
            e = e[1..^1];
        }
        return e;

        static int SkipString(string s, int i)
        {
            for (i++; i < s.Length; i++)
            {
                if (s[i] == '\\') i++;
                else if (s[i] == '"') return i;
            }
            return s.Length;
        }
    }

    string Out(NodeCtx c, string pinId)
    {
        var spec = NodeSpecs.Get(c.Node.Kind);
        if (spec is null) { Error(c, $"Неизвестная нода «{c.Node.Kind}»."); return "default"; }

        if (spec.Mode == NodeMode.Entry) return GraphTypes.Ident(pinId.StartsWith("p:") ? pinId[2..] : pinId);
        if (spec.Expr is not null)
        {
            if (++_pureDepth > 64)
            {
                _pureDepth = 0;
                Error(c, "Данные ходят по кругу: нода зависит сама от себя.");
                return "default";
            }
            try { return spec.Expr(this, c, pinId); }
            finally { _pureDepth--; }
        }
        if (_outs.TryGetValue(c.Node.Id + ":" + pinId, out var v)) return v;

        Error(c, $"Значение ноды «{spec.Title(c)}» нужно раньше, чем она выполнится. Поставьте её выше в потоке.");
        return "default";
    }

    /// <summary>Доступ к полю: obj.Field, Class.Field (static) или this.Field.</summary>
    public string FieldAccess(NodeCtx c)
    {
        string cls = c.Prop("Class"), field = c.Prop("Field");
        var f = GraphScope.FindField(_p, cls, field);
        if (f is null) { Error(c, $"Поле «{cls}.{field}» не найдено."); return "default"; }

        if (f.IsStatic || (_p.FindClass(cls)?.IsStatic ?? false)) return $"{GraphTypes.Ident(cls)}.{GraphTypes.Ident(field)}";
        if (HasWire(c, "target")) return $"{In(c, "target")}.{GraphTypes.Ident(field)}";
        if (!c.M.IsStatic && !c.Cls.IsStatic) return $"this.{GraphTypes.Ident(field)}";

        Error(c, "Для обычного поля подключите объект.");
        return "default";
    }
}
