namespace CsharpLearning.Core.Graph;

/// <summary>Короткий способ собрать граф в коде (для готовых примеров и проверок).</summary>
public sealed class GraphBuilder
{
    public GraphData Data { get; }
    int _n;

    public GraphBuilder(GraphData data) => Data = data;

    public string Entry => Data.Entry?.Id ?? throw new InvalidOperationException("нет Entry");

    public string Add(string kind, double x, double y, params (string key, string value)[] props)
    {
        var n = new NodeData { Id = kind.ToLowerInvariant() + (++_n), Kind = kind, X = x, Y = y };
        foreach (var (k, v) in props) n.Props[k] = v;
        Data.Nodes.Add(n);
        return n.Id;
    }

    /// <summary>Значение, введённое прямо в ноде для незаподключённого входа.</summary>
    public GraphBuilder Set(string node, string pin, string value)
    {
        Data.Find(node)!.Props["in:" + pin] = value;
        return this;
    }

    public GraphBuilder Wire(string from, string fromPin, string to, string toPin)
    {
        Data.Wires.Add(new WireData { FromNode = from, FromPin = fromPin, ToNode = to, ToPin = toPin });
        return this;
    }

    /// <summary>Поток: from.then → to.in.</summary>
    public GraphBuilder Flow(string from, string to, string fromPin = "then") => Wire(from, fromPin, to, "in");
}

public static class GraphSamples
{
    public sealed record Sample(string Title, string Description, Func<GraphProject> Build);

    public static readonly IReadOnlyList<Sample> All = new List<Sample>
    {
        new("Привет, мир", "Самый простой граф: Начало → Вывести строку.", Hello),
        new("Условие: что больше", "Две переменные, сравнение и ветвление if / else.", Compare),
        new("Таблица умножения", "Цикл for с формулой и шаблоном текста.", Table),
        new("Класс Игрок", "Свой класс с полем, конструктором и методами; объект создаётся и вызывается из Main.", Player),
        new("Факториал (рекурсия)", "Метод вызывает сам себя; ветка «да» возвращает 1.", Factorial),
    };

    static GraphProject Hello()
    {
        var p = GraphProject.CreateEmpty();
        p.Name = "Привет, мир";
        var b = new GraphBuilder(p.FindClass("Program")!.FindMethod("Main")!.Graph);
        var print = b.Add("Print", 360, 80);
        b.Set(print, "value", "Привет, мир!");
        b.Flow(b.Entry, print);
        return p;
    }

    static GraphProject Compare()
    {
        var p = GraphProject.CreateEmpty();
        p.Name = "Условие: что больше";
        var main = p.FindClass("Program")!.FindMethod("Main")!;
        main.Locals.Add(new LocalDef { Name = "a", Type = "int", Initial = "7" });
        main.Locals.Add(new LocalDef { Name = "b", Type = "int", Initial = "3" });
        var b = new GraphBuilder(main.Graph);

        var geta = b.Add("GetVar", 330, 250, ("Name", "a"));
        var getb = b.Add("GetVar", 330, 320, ("Name", "b"));
        var cmp = b.Add("Compare", 580, 270, ("Op", ">"), ("Type", "int"));
        var br = b.Add("Branch", 330, 60);

        var yes = b.Add("Print", 700, 20);
        b.Set(yes, "value", "a больше b");
        var no = b.Add("Print", 700, 190);
        b.Set(no, "value", "a не больше b");

        b.Flow(b.Entry, br);
        b.Wire(geta, "value", cmp, "a").Wire(getb, "value", cmp, "b").Wire(cmp, "result", br, "cond");
        b.Flow(br, yes, "true").Flow(br, no, "false");
        return p;
    }

    static GraphProject Table()
    {
        var p = GraphProject.CreateEmpty();
        p.Name = "Таблица умножения";
        var b = new GraphBuilder(p.FindClass("Program")!.FindMethod("Main")!.Graph);

        var loop = b.Add("ForRange", 330, 60, ("Var", "i"));
        b.Set(loop, "from", "1").Set(loop, "to", "11");

        var mul = b.Add("Op", 640, 260, ("Op", "*"), ("Type", "int"));
        b.Set(mul, "b", "7");
        b.Wire(loop, "index", mul, "a");

        var fmt = b.Add("Format", 880, 120, ("Template", "7 x {0} = {1}"));
        b.Wire(loop, "index", fmt, "a0").Wire(mul, "result", fmt, "a1");

        var print = b.Add("Print", 1160, 60);
        b.Wire(fmt, "result", print, "value");

        b.Flow(b.Entry, loop).Flow(loop, print, "body");
        return p;
    }

    static GraphProject Player()
    {
        var p = GraphProject.CreateEmpty();
        p.Name = "Класс Игрок";

        var player = new ClassDef { Name = "Player" };
        player.Fields.Add(new FieldDef { Name = "Name", Type = "string" });
        player.Fields.Add(new FieldDef { Name = "Hp", Type = "int", Initial = "100" });

        // конструктор: Name = name
        var ctor = new MethodDef { Name = MethodDef.CtorName };
        ctor.Params.Add(new ParamDef { Name = "name", Type = "string" });
        ctor.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 60, Y = 60 });
        var cb = new GraphBuilder(ctor.Graph);
        var setName = cb.Add("SetField", 330, 60, ("Class", "Player"), ("Field", "Name"));
        cb.Wire("entry", "p:name", setName, "value").Flow("entry", setName);
        player.Methods.Add(ctor);

        // TakeDamage(damage): Hp = Hp - damage
        var hit = new MethodDef { Name = "TakeDamage" };
        hit.Params.Add(new ParamDef { Name = "damage", Type = "int" });
        hit.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 60, Y = 60 });
        var hb = new GraphBuilder(hit.Graph);
        var hp = hb.Add("GetField", 330, 250, ("Class", "Player"), ("Field", "Hp"));
        var sub = hb.Add("Op", 600, 250, ("Op", "-"), ("Type", "int"));
        var setHp = hb.Add("SetField", 600, 60, ("Class", "Player"), ("Field", "Hp"));
        hb.Wire(hp, "value", sub, "a").Wire("entry", "p:damage", sub, "b").Wire(sub, "result", setHp, "value").Flow("entry", setHp);
        player.Methods.Add(hit);

        // IsAlive(): Hp > 0
        var alive = new MethodDef { Name = "IsAlive", ReturnType = "bool" };
        alive.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 60, Y = 60 });
        var ab = new GraphBuilder(alive.Graph);
        var hp2 = ab.Add("GetField", 330, 220, ("Class", "Player"), ("Field", "Hp"));
        var gt = ab.Add("Compare", 580, 220, ("Op", ">"), ("Type", "int"));
        ab.Wire(hp2, "value", gt, "a");
        var ret = ab.Add("Return", 820, 60);
        ab.Wire(gt, "result", ret, "value").Flow("entry", ret);
        player.Methods.Add(alive);

        p.Classes.Insert(0, player);

        // Main
        var main = p.FindClass("Program")!.FindMethod("Main")!;
        var mb = new GraphBuilder(main.Graph);
        var create = mb.Add("NewObject", 330, 60, ("Class", "Player"));
        mb.Set(create, "p:name", "Bobik");
        var damage = mb.Add("CallMethod", 640, 60, ("Class", "Player"), ("Method", "TakeDamage"));
        mb.Wire(create, "obj", damage, "target").Set(damage, "p:damage", "30");
        var check = mb.Add("CallMethod", 950, 60, ("Class", "Player"), ("Method", "IsAlive"));
        mb.Wire(create, "obj", check, "target");
        var print = mb.Add("Print", 1250, 60);
        mb.Wire(check, "result", print, "value");
        mb.Flow(mb.Entry, create).Flow(create, damage).Flow(damage, check).Flow(check, print);
        return p;
    }

    static GraphProject Factorial()
    {
        var p = GraphProject.CreateEmpty();
        p.Name = "Факториал (рекурсия)";

        var math = new ClassDef { Name = "MathUtil", IsStatic = true };
        var f = new MethodDef { Name = "Factorial", ReturnType = "long", IsStatic = true };
        f.Params.Add(new ParamDef { Name = "n", Type = "int" });
        f.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 60, Y = 80 });
        var b = new GraphBuilder(f.Graph);

        var cmp = b.Add("Compare", 330, 260, ("Op", "<="), ("Type", "int"));
        b.Wire("entry", "p:n", cmp, "a").Set(cmp, "b", "1");
        var br = b.Add("Branch", 330, 80);
        b.Wire(cmp, "result", br, "cond");

        var one = b.Add("Return", 640, 20);
        b.Set(one, "value", "1");

        var minus = b.Add("Op", 640, 330, ("Op", "-"), ("Type", "int"));
        b.Wire("entry", "p:n", minus, "a").Set(minus, "b", "1");
        var call = b.Add("CallMethod", 640, 150, ("Class", "MathUtil"), ("Method", "Factorial"));
        b.Wire(minus, "result", call, "p:n");
        var times = b.Add("Op", 940, 330, ("Op", "*"), ("Type", "long"));
        b.Wire("entry", "p:n", times, "a").Wire(call, "result", times, "b");
        var ret = b.Add("Return", 1200, 150);
        b.Wire(times, "result", ret, "value");

        b.Flow("entry", br).Flow(br, one, "true").Flow(br, call, "false").Flow(call, ret);
        math.Methods.Add(f);
        p.Classes.Insert(0, math);

        var main = p.FindClass("Program")!.FindMethod("Main")!;
        var mb = new GraphBuilder(main.Graph);
        var c1 = mb.Add("CallMethod", 330, 60, ("Class", "MathUtil"), ("Method", "Factorial"));
        mb.Set(c1, "p:n", "10");
        var print = mb.Add("Print", 640, 60);
        mb.Wire(c1, "result", print, "value");
        mb.Flow(mb.Entry, c1).Flow(c1, print);
        return p;
    }
}
