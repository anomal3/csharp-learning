using System.Text.RegularExpressions;

namespace CsharpLearning.Core.Graph;

/// <summary>Пин ноды: вход/выход потока выполнения (Exec) или данных (Data).</summary>
public sealed record Pin(string Id, string Label, bool IsExec, bool IsOutput, string Type, string Default = "");

public enum PropEditor { Text, Type, Choice, Bool, Class, Method, Field, Var }

public sealed record PropDef(string Name, string Label, PropEditor Editor, string Default = "", string[]? Choices = null, bool Inline = false);

/// <summary>Pure — вычисляется там, где нужно (как формула); Exec — шаг программы; Entry — начало метода.</summary>
public enum NodeMode { Pure, Exec, Entry }

public sealed class NodeCtx
{
    public NodeCtx(NodeData node, GraphProject p, ClassDef cls, MethodDef m) { Node = node; P = p; Cls = cls; M = m; }
    public NodeData Node { get; }
    public GraphProject P { get; }
    public ClassDef Cls { get; }
    public MethodDef M { get; }

    public string Prop(string name) =>
        Node.Props.TryGetValue(name, out var v) ? v : NodeSpecs.Get(Node.Kind)?.Props.FirstOrDefault(x => x.Name == name)?.Default ?? "";
}

public sealed class NodeSpec
{
    public string Kind { get; init; } = "";
    public string Category { get; init; } = "";
    public string Color { get; init; } = "#4A5568";
    public string Icon { get; init; } = "";
    public NodeMode Mode { get; init; }
    public Func<NodeCtx, string> Title { get; init; } = _ => "";
    public Func<NodeCtx, List<Pin>> Pins { get; init; } = _ => new();
    public List<PropDef> Props { get; init; } = new();

    /// <summary>Exec-нода: пишет операторы, возвращает id выхода потока, куда идти дальше (null — конец ветки).</summary>
    public Func<GraphCodeGen, NodeCtx, string?>? Emit { get; init; }

    /// <summary>Pure-нода: выражение для выходного пина.</summary>
    public Func<GraphCodeGen, NodeCtx, string, string>? Expr { get; init; }

    public bool Deletable => Mode != NodeMode.Entry;
}

public sealed record PaletteItem(string Title, string Category, string Kind, Dictionary<string, string> Props, string Hint = "");

/// <summary>Описание встроенной функции .NET, которую можно поставить на граф как ноду.</summary>
public sealed record Builtin(string Id, string Title, string Category, NodeMode Mode, string Ret,
    (string name, string type)[] Params, string Template, string Hint = "");

public static partial class NodeSpecs
{
    static readonly Dictionary<string, NodeSpec> Registry = new();
    public static IEnumerable<NodeSpec> All => Registry.Values;
    public static NodeSpec? Get(string kind) => Registry.TryGetValue(kind, out var s) ? s : null;

    // ---------------- помощники для описания пинов ----------------
    static Pin XIn(string id = "in", string label = "") => new(id, label, true, false, "");
    static Pin XOut(string id, string label = "") => new(id, label, true, true, "");
    static Pin DIn(string id, string label, string type, string def = "") => new(id, label, false, false, type, def);
    static Pin DOut(string id, string label, string type) => new(id, label, false, true, type);

    static string CatFlow => "Поток";
    static string CatVar => "Переменные";
    static string CatMath => "Математика";
    static string CatLogic => "Логика";
    static string CatText => "Текст";
    static string CatCall => "Вызовы";
    static string CatList => "Списки";

    static NodeSpecs()
    {
        Add(new NodeSpec
        {
            Kind = "Entry", Category = CatFlow, Color = "#3DCB8A", Icon = "▶", Mode = NodeMode.Entry,
            Title = c => c.M.IsCtor ? "Конструктор" : "Начало: " + c.M.Name,
            Pins = c =>
            {
                var l = new List<Pin> { XOut("then") };
                foreach (var p in c.M.Params) l.Add(DOut("p:" + p.Name, p.Name, p.Type));
                return l;
            },
            Emit = (g, c) => "then",
        });

        Add(new NodeSpec
        {
            Kind = "Return", Category = CatFlow, Color = "#E9573F", Icon = "⏎", Mode = NodeMode.Exec,
            Title = _ => "Вернуть значение",
            Pins = c =>
            {
                var l = new List<Pin> { XIn() };
                if (!c.M.IsVoid) l.Add(DIn("value", "значение", c.M.ReturnType, GraphTypes.Default(c.M.ReturnType)));
                return l;
            },
            Emit = (g, c) =>
            {
                g.Line(c.M.IsVoid ? "return;" : $"return {g.InTop(c, "value")};");
                return null;
            },
        });

        Add(new NodeSpec
        {
            Kind = "Branch", Category = CatFlow, Color = "#F5A623", Icon = "⑂", Mode = NodeMode.Exec,
            Title = _ => "Если (if / else)",
            Pins = _ => new List<Pin>
            {
                XIn(), DIn("cond", "условие", "bool", "true"),
                XOut("true", "да"), XOut("false", "нет"), XOut("next", "дальше"),
            },
            Emit = (g, c) =>
            {
                g.Line($"if ({g.InTop(c, "cond")})");
                g.Block(c, "true");
                if (g.HasTarget(c, "false"))
                {
                    g.Line("else");
                    g.Block(c, "false");
                }
                return "next";
            },
        });

        Add(new NodeSpec
        {
            Kind = "ForRange", Category = CatFlow, Color = "#F5A623", Icon = "↻", Mode = NodeMode.Exec,
            Props = { new("Var", "Имя счётчика", PropEditor.Text, "i", null, true) },
            Title = c => $"Повторять (for {c.Prop("Var")})",
            Pins = c => new List<Pin>
            {
                XIn(), DIn("from", "от", "int", "0"), DIn("to", "до (не включая)", "int", "10"),
                XOut("body", "тело"), XOut("done", "после цикла"), DOut("index", c.Prop("Var"), "int"),
            },
            Emit = (g, c) =>
            {
                string v = GraphTypes.Ident(c.Prop("Var"), "i");
                g.Line($"for (int {v} = {g.InTop(c, "from")}; {v} < {g.InTop(c, "to")}; {v}++)");
                g.Block(c, "body");
                return "done";
            },
            Expr = (g, c, pin) => GraphTypes.Ident(c.Prop("Var"), "i"),
        });

        Add(new NodeSpec
        {
            Kind = "ForEach", Category = CatFlow, Color = "#F5A623", Icon = "∀", Mode = NodeMode.Exec,
            Props = { new("T", "Тип элемента", PropEditor.Type, "int"), new("Var", "Имя элемента", PropEditor.Text, "item", null, true) },
            Title = c => $"Для каждого (foreach {c.Prop("Var")})",
            Pins = c => new List<Pin>
            {
                XIn(), DIn("list", "список", $"List<{c.Prop("T")}>"),
                XOut("body", "тело"), XOut("done", "после цикла"), DOut("item", c.Prop("Var"), c.Prop("T")),
            },
            Emit = (g, c) =>
            {
                string v = GraphTypes.Ident(c.Prop("Var"), "item");
                g.Line($"foreach ({c.Prop("T")} {v} in {g.In(c, "list")})");
                g.Block(c, "body");
                return "done";
            },
            Expr = (g, c, pin) => GraphTypes.Ident(c.Prop("Var"), "item"),
        });

        Add(new NodeSpec
        {
            Kind = "While", Category = CatFlow, Color = "#F5A623", Icon = "⟳", Mode = NodeMode.Exec,
            Title = _ => "Пока условие (while)",
            Pins = _ => new List<Pin> { XIn(), DIn("cond", "условие", "bool", "true"), XOut("body", "тело"), XOut("done", "после цикла") },
            Emit = (g, c) =>
            {
                g.Line($"while ({g.InTop(c, "cond")})");
                g.Block(c, "body");
                return "done";
            },
        });

        Add(new NodeSpec
        {
            Kind = "Break", Category = CatFlow, Color = "#E9573F", Icon = "⏹", Mode = NodeMode.Exec,
            Title = _ => "Выйти из цикла (break)",
            Pins = _ => new List<Pin> { XIn() },
            Emit = (g, c) => { g.Line("break;"); return null; },
        });

        Add(new NodeSpec
        {
            Kind = "Continue", Category = CatFlow, Color = "#E9573F", Icon = "⏭", Mode = NodeMode.Exec,
            Title = _ => "Следующий шаг (continue)",
            Pins = _ => new List<Pin> { XIn() },
            Emit = (g, c) => { g.Line("continue;"); return null; },
        });

        // ---------------- переменные ----------------
        Add(new NodeSpec
        {
            Kind = "DeclareVar", Category = CatVar, Color = "#4FC1E9", Icon = "▣", Mode = NodeMode.Exec,
            Props = { new("Name", "Имя", PropEditor.Text, "x", null, true), new("Type", "Тип", PropEditor.Type, "int") },
            Title = c => $"Создать переменную {c.Prop("Name")}",
            Pins = c => new List<Pin>
            {
                XIn(), DIn("value", "значение", c.Prop("Type"), GraphTypes.Default(c.Prop("Type"))),
                XOut("then"), DOut("var", c.Prop("Name"), c.Prop("Type")),
            },
            Emit = (g, c) =>
            {
                g.Line($"{c.Prop("Type")} {GraphTypes.Ident(c.Prop("Name"))} = {g.InTop(c, "value")};");
                return "then";
            },
            Expr = (g, c, pin) => GraphTypes.Ident(c.Prop("Name")),
        });

        Add(new NodeSpec
        {
            Kind = "SetVar", Category = CatVar, Color = "#4FC1E9", Icon = "←", Mode = NodeMode.Exec,
            Props = { new("Name", "Переменная", PropEditor.Var, "") },
            Title = c => $"Присвоить {c.Prop("Name")}",
            Pins = c => new List<Pin>
            {
                XIn(), DIn("value", "значение", GraphScope.TypeOf(c.P, c.Cls, c.M, c.Prop("Name")) ?? "object"), XOut("then"),
            },
            Emit = (g, c) =>
            {
                string name = c.Prop("Name");
                if (GraphScope.TypeOf(c.P, c.Cls, c.M, name) is null) g.Error(c, $"Переменная «{name}» не найдена.");
                g.Line($"{GraphTypes.Ident(name)} = {g.InTop(c, "value")};");
                return "then";
            },
        });

        Add(new NodeSpec
        {
            Kind = "GetVar", Category = CatVar, Color = "#4FC1E9", Icon = "→", Mode = NodeMode.Pure,
            Props = { new("Name", "Переменная", PropEditor.Var, "") },
            Title = c => c.Prop("Name").Length == 0 ? "Значение переменной" : c.Prop("Name"),
            Pins = c => new List<Pin> { DOut("value", "", GraphScope.TypeOf(c.P, c.Cls, c.M, c.Prop("Name")) ?? "object") },
            Expr = (g, c, pin) =>
            {
                string name = c.Prop("Name");
                if (GraphScope.TypeOf(c.P, c.Cls, c.M, name) is null) g.Error(c, $"Переменная «{name}» не найдена.");
                return GraphTypes.Ident(name);
            },
        });

        Add(new NodeSpec
        {
            Kind = "This", Category = CatVar, Color = "#48CFAD", Icon = "◉", Mode = NodeMode.Pure,
            Title = c => "Этот объект (this)",
            Pins = c => new List<Pin> { DOut("value", "", c.Cls.Name) },
            Expr = (g, c, pin) => "this",
        });

        Add(new NodeSpec
        {
            Kind = "GetField", Category = CatVar, Color = "#48CFAD", Icon = "⊡", Mode = NodeMode.Pure,
            Props = { new("Class", "Класс", PropEditor.Class, ""), new("Field", "Поле", PropEditor.Field, "") },
            Title = c => $"Взять {c.Prop("Class")}.{c.Prop("Field")}",
            Pins = c =>
            {
                var l = new List<Pin>();
                var f = GraphScope.FindField(c.P, c.Prop("Class"), c.Prop("Field"));
                if (f is not null && !f.IsStatic) l.Add(DIn("target", "объект", c.Prop("Class")));
                l.Add(DOut("value", "", f?.Type ?? "object"));
                return l;
            },
            Expr = (g, c, pin) => g.FieldAccess(c),
        });

        Add(new NodeSpec
        {
            Kind = "SetField", Category = CatVar, Color = "#48CFAD", Icon = "⊡", Mode = NodeMode.Exec,
            Props = { new("Class", "Класс", PropEditor.Class, ""), new("Field", "Поле", PropEditor.Field, "") },
            Title = c => $"Записать {c.Prop("Class")}.{c.Prop("Field")}",
            Pins = c =>
            {
                var l = new List<Pin> { XIn() };
                var f = GraphScope.FindField(c.P, c.Prop("Class"), c.Prop("Field"));
                if (f is not null && !f.IsStatic) l.Add(DIn("target", "объект", c.Prop("Class")));
                l.Add(DIn("value", "значение", f?.Type ?? "object"));
                l.Add(XOut("then"));
                return l;
            },
            Emit = (g, c) =>
            {
                g.Line($"{g.FieldAccess(c)} = {g.InTop(c, "value")};");
                return "then";
            },
        });

        Add(new NodeSpec
        {
            Kind = "Literal", Category = CatVar, Color = "#7B8794", Icon = "#", Mode = NodeMode.Pure,
            Props = { new("Type", "Тип", PropEditor.Choice, "int", GraphTypes.Primitive), new("Value", "Значение", PropEditor.Text, "0", null, true) },
            Title = c => "Значение " + c.Prop("Type"),
            Pins = c => new List<Pin> { DOut("value", "", c.Prop("Type")) },
            Expr = (g, c, pin) => GraphTypes.Literal(c.Prop("Type"), c.Prop("Value")),
        });

        // ---------------- математика и логика ----------------
        Add(new NodeSpec
        {
            Kind = "Op", Category = CatMath, Color = "#8CD867", Icon = "±", Mode = NodeMode.Pure,
            Props =
            {
                new("Op", "Действие", PropEditor.Choice, "+", new[] { "+", "-", "*", "/", "%" }),
                new("Type", "Тип", PropEditor.Choice, "int", new[] { "int", "long", "double", "decimal", "string" }),
            },
            Title = c => c.Prop("Op") switch { "+" => "Сложить", "-" => "Вычесть", "*" => "Умножить", "/" => "Разделить", _ => "Остаток от деления" } + $" ({c.Prop("Type")})",
            Pins = c => new List<Pin>
            {
                DIn("a", "A", c.Prop("Type"), GraphTypes.Default(c.Prop("Type"))),
                DIn("b", "B", c.Prop("Type"), c.Prop("Op") is "*" or "/" ? "1" : GraphTypes.Default(c.Prop("Type"))),
                DOut("result", "результат", c.Prop("Type")),
            },
            Expr = (g, c, pin) => $"({g.In(c, "a")} {c.Prop("Op")} {g.In(c, "b")})",
        });

        Add(new NodeSpec
        {
            Kind = "Compare", Category = CatLogic, Color = "#E9573F", Icon = "⇄", Mode = NodeMode.Pure,
            Props =
            {
                new("Op", "Сравнение", PropEditor.Choice, "==", new[] { "==", "!=", "<", ">", "<=", ">=" }),
                new("Type", "Тип", PropEditor.Choice, "int", new[] { "int", "long", "double", "decimal", "string", "char", "bool" }),
            },
            Title = c => "Сравнить " + c.Prop("Op"),
            Pins = c => new List<Pin>
            {
                DIn("a", "A", c.Prop("Type"), GraphTypes.Default(c.Prop("Type"))),
                DIn("b", "B", c.Prop("Type"), GraphTypes.Default(c.Prop("Type"))),
                DOut("result", "да/нет", "bool"),
            },
            Expr = (g, c, pin) => $"({g.In(c, "a")} {c.Prop("Op")} {g.In(c, "b")})",
        });

        Add(new NodeSpec
        {
            Kind = "Logic", Category = CatLogic, Color = "#E9573F", Icon = "∧", Mode = NodeMode.Pure,
            Props = { new("Op", "Операция", PropEditor.Choice, "&&", new[] { "&&", "||" }) },
            Title = c => c.Prop("Op") == "&&" ? "И (оба истинны)" : "ИЛИ (хотя бы одно)",
            Pins = _ => new List<Pin> { DIn("a", "A", "bool", "false"), DIn("b", "B", "bool", "false"), DOut("result", "да/нет", "bool") },
            Expr = (g, c, pin) => $"({g.In(c, "a")} {c.Prop("Op")} {g.In(c, "b")})",
        });

        Add(new NodeSpec
        {
            Kind = "Not", Category = CatLogic, Color = "#E9573F", Icon = "¬", Mode = NodeMode.Pure,
            Title = _ => "НЕ (инверсия)",
            Pins = _ => new List<Pin> { DIn("a", "значение", "bool", "false"), DOut("result", "да/нет", "bool") },
            Expr = (g, c, pin) => $"(!{g.In(c, "a")})",
        });

        Add(new NodeSpec
        {
            Kind = "Convert", Category = CatText, Color = "#F6BB42", Icon = "⇒", Mode = NodeMode.Pure,
            Props = { new("To", "Во что", PropEditor.Choice, "string", new[] { "string", "int", "long", "double", "decimal", "bool" }) },
            Title = c => "Преобразовать в " + c.Prop("To"),
            Pins = c => new List<Pin> { DIn("value", "значение", "object"), DOut("result", "результат", c.Prop("To")) },
            Expr = (g, c, pin) => c.Prop("To") switch
            {
                "string" => $"Convert.ToString({g.In(c, "value")})",
                "int" => $"Convert.ToInt32({g.In(c, "value")})",
                "long" => $"Convert.ToInt64({g.In(c, "value")})",
                "double" => $"Convert.ToDouble({g.In(c, "value")})",
                "decimal" => $"Convert.ToDecimal({g.In(c, "value")})",
                _ => $"Convert.ToBoolean({g.In(c, "value")})",
            },
        });

        Add(new NodeSpec
        {
            Kind = "Format", Category = CatText, Color = "#F6BB42", Icon = "{}", Mode = NodeMode.Pure,
            Props = { new("Template", "Шаблон ({0}, {1}…)", PropEditor.Text, "Привет, {0}!", null, true) },
            Title = _ => "Собрать текст по шаблону",
            Pins = c =>
            {
                var l = new List<Pin>();
                for (int i = 0; i < FormatCount(c.Prop("Template")); i++) l.Add(DIn("a" + i, "{" + i + "}", "object"));
                l.Add(DOut("result", "текст", "string"));
                return l;
            },
            Expr = (g, c, pin) =>
            {
                int n = FormatCount(c.Prop("Template"));
                string args = string.Concat(Enumerable.Range(0, n).Select(i => ", " + g.InTop(c, "a" + i)));
                return n == 0 ? GraphTypes.Quote(c.Prop("Template")) : $"string.Format({GraphTypes.Quote(c.Prop("Template"))}{args})";
            },
        });

        // ---------------- ввод-вывод ----------------
        Add(new NodeSpec
        {
            Kind = "Print", Category = CatCall, Color = "#9B6AF5", Icon = "⎘", Mode = NodeMode.Exec,
            Props = { new("NewLine", "С новой строки", PropEditor.Bool, "true") },
            Title = c => c.Prop("NewLine") == "true" ? "Вывести строку (WriteLine)" : "Вывести без переноса (Write)",
            Pins = _ => new List<Pin> { XIn(), DIn("value", "значение", "object", ""), XOut("then") },
            Emit = (g, c) =>
            {
                g.Line($"Console.{(c.Prop("NewLine") == "true" ? "WriteLine" : "Write")}({g.InTop(c, "value")});");
                return "then";
            },
        });

        Add(new NodeSpec
        {
            Kind = "ReadLine", Category = CatCall, Color = "#9B6AF5", Icon = "⌨", Mode = NodeMode.Exec,
            Title = _ => "Прочитать строку (ReadLine)",
            Pins = _ => new List<Pin> { XIn(), XOut("then"), DOut("text", "текст", "string") },
            Emit = (g, c) =>
            {
                string v = g.NewTemp("text");
                g.Line($"string {v} = Console.ReadLine() ?? \"\";");
                g.SetOut(c, "text", v);
                return "then";
            },
        });

        // ---------------- классы проекта ----------------
        Add(new NodeSpec
        {
            Kind = "CallMethod", Category = CatCall, Color = "#9B6AF5", Icon = "ƒ", Mode = NodeMode.Exec,
            Props = { new("Class", "Класс", PropEditor.Class, ""), new("Method", "Метод", PropEditor.Method, "") },
            Title = c => $"Вызвать {c.Prop("Class")}.{c.Prop("Method")}",
            Pins = c =>
            {
                var l = new List<Pin> { XIn() };
                var m = GraphScope.FindMethod(c.P, c.Prop("Class"), c.Prop("Method"));
                if (m is not null && !m.IsStatic) l.Add(DIn("target", "объект", c.Prop("Class")));
                if (m is not null)
                    foreach (var p in m.Params) l.Add(DIn("p:" + p.Name, p.Name, p.Type, GraphTypes.Default(p.Type)));
                l.Add(XOut("then"));
                if (m is not null && !m.IsVoid) l.Add(DOut("result", "результат", m.ReturnType));
                return l;
            },
            Emit = (g, c) =>
            {
                var m = GraphScope.FindMethod(c.P, c.Prop("Class"), c.Prop("Method"));
                if (m is null) { g.Error(c, $"Метод «{c.Prop("Class")}.{c.Prop("Method")}» не найден."); return "then"; }

                string args = string.Join(", ", m.Params.Select(p => g.InTop(c, "p:" + p.Name)));
                string target;
                if (m.IsStatic) target = c.Prop("Class");
                else if (g.HasWire(c, "target")) target = g.In(c, "target");
                else if (c.Cls.Name == c.Prop("Class") && !c.M.IsStatic) target = "this";
                else { g.Error(c, "Для обычного (не static) метода подключите объект."); target = "null"; }

                string call = $"{target}.{GraphTypes.Ident(m.Name)}({args})";
                if (m.IsVoid) g.Line(call + ";");
                else
                {
                    string v = g.NewTemp("result");
                    g.Line($"{m.ReturnType} {v} = {call};");
                    g.SetOut(c, "result", v);
                }
                return "then";
            },
        });

        Add(new NodeSpec
        {
            Kind = "NewObject", Category = CatCall, Color = "#48CFAD", Icon = "✚", Mode = NodeMode.Exec,
            Props = { new("Class", "Класс", PropEditor.Class, "") },
            Title = c => $"Создать объект {c.Prop("Class")}",
            Pins = c =>
            {
                var l = new List<Pin> { XIn() };
                var ctor = c.P.FindClass(c.Prop("Class"))?.Constructor;
                if (ctor is not null)
                    foreach (var p in ctor.Params) l.Add(DIn("p:" + p.Name, p.Name, p.Type, GraphTypes.Default(p.Type)));
                l.Add(XOut("then"));
                l.Add(DOut("obj", "объект", c.Prop("Class")));
                return l;
            },
            Emit = (g, c) =>
            {
                var cls = c.P.FindClass(c.Prop("Class"));
                if (cls is null) { g.Error(c, "Класс не выбран."); return "then"; }
                var ctor = cls.Constructor;
                string args = ctor is null ? "" : string.Join(", ", ctor.Params.Select(p => g.InTop(c, "p:" + p.Name)));
                string v = g.NewTemp(char.ToLowerInvariant(cls.Name[0]) + cls.Name[1..]);
                g.Line($"{GraphTypes.Ident(cls.Name)} {v} = new {GraphTypes.Ident(cls.Name)}({args});");
                g.SetOut(c, "obj", v);
                return "then";
            },
        });

        RegisterBuiltins();
    }

    static int FormatCount(string template)
    {
        int max = -1;
        foreach (Match m in Regex.Matches(template ?? "", @"\{(\d+)\}"))
            max = Math.Max(max, int.Parse(m.Groups[1].Value));
        return Math.Min(max + 1, 8);
    }

    static void Add(NodeSpec s) => Registry[s.Kind] = s;
}
