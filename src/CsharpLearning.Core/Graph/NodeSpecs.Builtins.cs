namespace CsharpLearning.Core.Graph;

/// <summary>Встроенные функции .NET и палитра нод, которую видит ученик.</summary>
public static partial class NodeSpecs
{
    static string[] None => Array.Empty<string>();

    static (string, string)[] P(params (string, string)[] p) => p;

    public static readonly List<Builtin> Builtins = new()
    {
        // ---- математика ----
        new("math.abs", "Модуль числа", "Математика", NodeMode.Pure, "double", P(("x", "double")), "Math.Abs({0})"),
        new("math.sqrt", "Квадратный корень", "Математика", NodeMode.Pure, "double", P(("x", "double")), "Math.Sqrt({0})"),
        new("math.pow", "Степень", "Математика", NodeMode.Pure, "double", P(("x", "double"), ("y", "double")), "Math.Pow({0}, {1})"),
        new("math.max", "Большее из двух", "Математика", NodeMode.Pure, "double", P(("a", "double"), ("b", "double")), "Math.Max({0}, {1})"),
        new("math.min", "Меньшее из двух", "Математика", NodeMode.Pure, "double", P(("a", "double"), ("b", "double")), "Math.Min({0}, {1})"),
        new("math.round", "Округлить", "Математика", NodeMode.Pure, "double", P(("x", "double")), "Math.Round({0})"),
        new("math.floor", "Округлить вниз", "Математика", NodeMode.Pure, "double", P(("x", "double")), "Math.Floor({0})"),
        new("math.ceil", "Округлить вверх", "Математика", NodeMode.Pure, "double", P(("x", "double")), "Math.Ceiling({0})"),
        new("math.pi", "Число π", "Математика", NodeMode.Pure, "double", P(), "Math.PI"),
        new("rand.int", "Случайное целое", "Математика", NodeMode.Pure, "int", P(("от", "int"), ("до", "int")), "Random.Shared.Next({0}, {1})", "от — включительно, до — не включая"),
        new("rand.dbl", "Случайное от 0 до 1", "Математика", NodeMode.Pure, "double", P(), "Random.Shared.NextDouble()"),

        // ---- текст ----
        new("str.len", "Длина текста", "Текст", NodeMode.Pure, "int", P(("текст", "string")), "{0}.Length"),
        new("str.upper", "В ЗАГЛАВНЫЕ", "Текст", NodeMode.Pure, "string", P(("текст", "string")), "{0}.ToUpper()"),
        new("str.lower", "в строчные", "Текст", NodeMode.Pure, "string", P(("текст", "string")), "{0}.ToLower()"),
        new("str.trim", "Убрать пробелы по краям", "Текст", NodeMode.Pure, "string", P(("текст", "string")), "{0}.Trim()"),
        new("str.contains", "Содержит ли текст", "Текст", NodeMode.Pure, "bool", P(("текст", "string"), ("часть", "string")), "{0}.Contains({1})"),
        new("str.replace", "Заменить в тексте", "Текст", NodeMode.Pure, "string", P(("текст", "string"), ("что", "string"), ("на что", "string")), "{0}.Replace({1}, {2})"),
        new("str.sub", "Часть текста", "Текст", NodeMode.Pure, "string", P(("текст", "string"), ("с номера", "int"), ("длина", "int")), "{0}.Substring({1}, {2})"),
        new("str.concat", "Склеить два текста", "Текст", NodeMode.Pure, "string", P(("A", "string"), ("B", "string")), "({0} + {1})"),
        new("str.isempty", "Текст пустой", "Текст", NodeMode.Pure, "bool", P(("текст", "string")), "string.IsNullOrEmpty({0})"),
        new("str.char", "Символ по номеру", "Текст", NodeMode.Pure, "char", P(("текст", "string"), ("номер", "int")), "{0}[{1}]"),
        new("str.repeat", "Повторить текст", "Текст", NodeMode.Pure, "string", P(("текст", "string"), ("раз", "int")), "string.Concat(Enumerable.Repeat({0}, {1}))"),
        new("time.now", "Время сейчас", "Текст", NodeMode.Pure, "string", P(), "DateTime.Now.ToString(\"HH:mm:ss\")"),

        // ---- списки ----
        new("list.new", "Новый список", "Списки", NodeMode.Exec, "List<{T}>", P(), "new List<{T}>()"),
        new("list.add", "Добавить в список", "Списки", NodeMode.Exec, "void", P(("список", "List<{T}>"), ("элемент", "{T}")), "{0}.Add({1});"),
        new("list.count", "Сколько в списке", "Списки", NodeMode.Pure, "int", P(("список", "List<{T}>")), "{0}.Count"),
        new("list.get", "Элемент списка по номеру", "Списки", NodeMode.Pure, "{T}", P(("список", "List<{T}>"), ("номер", "int")), "{0}[{1}]"),
        new("list.set", "Заменить элемент списка", "Списки", NodeMode.Exec, "void", P(("список", "List<{T}>"), ("номер", "int"), ("значение", "{T}")), "{0}[{1}] = {2};"),
        new("list.contains", "Есть ли в списке", "Списки", NodeMode.Pure, "bool", P(("список", "List<{T}>"), ("элемент", "{T}")), "{0}.Contains({1})"),
        new("list.removeat", "Удалить по номеру", "Списки", NodeMode.Exec, "void", P(("список", "List<{T}>"), ("номер", "int")), "{0}.RemoveAt({1});"),
        new("list.clear", "Очистить список", "Списки", NodeMode.Exec, "void", P(("список", "List<{T}>")), "{0}.Clear();"),
        new("list.sort", "Отсортировать список", "Списки", NodeMode.Exec, "void", P(("список", "List<{T}>")), "{0}.Sort();"),
        new("list.join", "Список в текст через разделитель", "Списки", NodeMode.Pure, "string", P(("список", "List<{T}>"), ("разделитель", "string")), "string.Join({1}, {0})"),
    };

    static void RegisterBuiltins()
    {
        foreach (var b in Builtins)
        {
            var bb = b;
            bool generic = bb.Template.Contains("{T}") || bb.Ret.Contains("{T}") || bb.Params.Any(p => p.type.Contains("{T}"));
            string Tp(NodeCtx c, string s) => s.Replace("{T}", c.Prop("T"));

            var props = new List<PropDef>();
            if (generic) props.Add(new PropDef("T", "Тип элементов", PropEditor.Type, "int"));

            Add(new NodeSpec
            {
                Kind = "fn." + bb.Id, Category = bb.Category, Mode = bb.Mode,
                Color = bb.Category switch { "Математика" => "#8CD867", "Текст" => "#F6BB42", _ => "#AC92EC" },
                Icon = bb.Category switch { "Математика" => "∑", "Текст" => "Aa", _ => "☰" },
                Props = props,
                Title = c => generic ? $"{bb.Title} ({c.Prop("T")})" : bb.Title,
                Pins = c =>
                {
                    var l = new List<Pin>();
                    if (bb.Mode == NodeMode.Exec) l.Add(XIn());
                    foreach (var (name, type) in bb.Params)
                    {
                        string t = Tp(c, type);
                        l.Add(DIn(name, name, t, GraphTypes.Default(t)));
                    }
                    if (bb.Mode == NodeMode.Exec) l.Add(XOut("then"));
                    if (bb.Ret != "void") l.Add(DOut("result", "", Tp(c, bb.Ret)));
                    return l;
                },
                Expr = bb.Mode == NodeMode.Pure ? (g, c, pin) => Fill(g, c, bb, Tp(c, bb.Template)) : null,
                Emit = bb.Mode == NodeMode.Exec ? (g, c) =>
                {
                    string code = Fill(g, c, bb, Tp(c, bb.Template));
                    if (bb.Ret == "void")
                    {
                        g.Line(code.EndsWith(';') ? code : code + ";");
                    }
                    else
                    {
                        string v = g.NewTemp("list");
                        g.Line($"{Tp(c, bb.Ret)} {v} = {code};");
                        g.SetOut(c, "result", v);
                    }
                    return "then";
                } : null,
            });
        }
    }

    static string Fill(GraphCodeGen g, NodeCtx c, Builtin b, string template)
    {
        for (int i = 0; i < b.Params.Length; i++)
            template = template.Replace("{" + i + "}", g.In(c, b.Params[i].name));
        return template;
    }

    /// <summary>Что делает нода — простыми словами (для панели свойств).</summary>
    public static string Describe(string kind) => kind switch
    {
        "Entry" => "Отсюда начинается выполнение метода. Белая стрелка справа — первое, что произойдёт. Параметры метода выходят цветными кружками.",
        "Return" => "Завершает метод. Если метод должен вернуть значение — подключите его к кружку «значение».",
        "Branch" => "Ветвление if / else. Условие — цветной кружок «bool». Дальше выполнение пойдёт по стрелке «да» или «нет». «дальше» — что делать после ветвления.",
        "ForRange" => "Цикл for: повторяет «тело» для каждого числа от «от» до «до» (не включая). Текущее число выходит кружком-счётчиком.",
        "ForEach" => "Цикл foreach: «тело» выполняется для каждого элемента списка. Элемент выходит кружком.",
        "While" => "Цикл while: «тело» повторяется, пока условие верно. Следите, чтобы цикл когда-нибудь закончился.",
        "Break" => "Немедленно выходит из ближайшего цикла.",
        "Continue" => "Пропускает остаток тела цикла и переходит к следующему шагу.",
        "GetVar" => "Отдаёт текущее значение переменной или параметра. Ставится там, где значение нужно как данные.",
        "SetVar" => "Записывает новое значение в переменную. Идёт в потоке выполнения (белые стрелки).",
        "This" => "Ссылка на объект, чей метод сейчас выполняется (this).",
        "GetField" => "Читает поле объекта. Для обычного (не static) поля подключите объект; внутри метода этого же класса можно оставить пустым.",
        "SetField" => "Записывает значение в поле объекта.",
        "Literal" => "Постоянное значение: число, текст, да/нет или символ. Значение вводится прямо в ноде.",
        "Op" => "Арифметика: + − × ÷ и остаток от деления. Для текста «+» склеивает строки.",
        "Compare" => "Сравнивает два значения и отдаёт да/нет (bool). Подключите результат к условию «Если».",
        "Logic" => "Объединяет два условия: И — оба верны, ИЛИ — хотя бы одно.",
        "Not" => "Переворачивает да/нет: было верно — станет неверно.",
        "Convert" => "Превращает значение в другой тип: например число в текст или текст в число.",
        "Format" => "Собирает текст из шаблона. Для каждого {0}, {1}… появится свой входной кружок.",
        "Print" => "Печатает значение в консоль. Главный способ увидеть результат программы.",
        "ReadLine" => "Ждёт, пока пользователь введёт строку в консоль, и отдаёт её. В этом редакторе ввода нет — вернётся пустая строка.",
        "CallMethod" => "Вызывает метод вашего класса. Кружки слева — аргументы, справа — результат.",
        "NewObject" => "Создаёт объект класса (new). Если у класса есть конструктор — его параметры появятся входными кружками.",
        _ when kind.StartsWith("fn.") => Builtins.FirstOrDefault(b => "fn." + b.Id == kind)?.Hint is { Length: > 0 } h ? h : "Готовая функция .NET: подключите входы и берите результат справа.",
        _ => "",
    };

    // ---------------- палитра ----------------

    /// <summary>Что можно добавить на граф метода: общие ноды, переменные в области видимости, вызовы методов проекта.</summary>
    public static List<PaletteItem> Palette(GraphProject p, ClassDef cls, MethodDef m)
    {
        var items = new List<PaletteItem>();
        void Add(string title, string cat, string kind, Dictionary<string, string>? props = null, string hint = "")
            => items.Add(new PaletteItem(title, cat, kind, props ?? new(), hint));

        // поток
        Add("Если (if / else)", "Поток", "Branch", null, "Ветвление: «да» — условие верно, «нет» — неверно");
        Add("Повторять N раз (for)", "Поток", "ForRange", null, "Цикл со счётчиком: от… до…");
        Add("Для каждого элемента списка (foreach)", "Поток", "ForEach", null, "Перебирает список по одному элементу");
        Add("Пока условие истинно (while)", "Поток", "While", null, "Повторяет, пока условие верно");
        Add("Выйти из цикла (break)", "Поток", "Break");
        Add("Следующий шаг цикла (continue)", "Поток", "Continue");
        Add("Вернуть значение (return)", "Поток", "Return", null, "Завершает метод и отдаёт результат");

        // вывод
        Add("Вывести строку (WriteLine)", "Ввод-вывод", "Print", new() { ["NewLine"] = "true" }, "Печатает значение в консоль и переходит на новую строку");
        Add("Вывести без переноса (Write)", "Ввод-вывод", "Print", new() { ["NewLine"] = "false" });
        Add("Прочитать строку (ReadLine)", "Ввод-вывод", "ReadLine");

        // значения
        Add("Число (int)", "Значения", "Literal", new() { ["Type"] = "int", ["Value"] = "0" });
        Add("Дробное число (double)", "Значения", "Literal", new() { ["Type"] = "double", ["Value"] = "0" });
        Add("Текст (string)", "Значения", "Literal", new() { ["Type"] = "string", ["Value"] = "" });
        Add("Да / нет (bool)", "Значения", "Literal", new() { ["Type"] = "bool", ["Value"] = "true" });
        Add("Символ (char)", "Значения", "Literal", new() { ["Type"] = "char", ["Value"] = "a" });

        // переменные
        foreach (var v in GraphScope.Vars(p, cls, m))
        {
            Add($"Значение {v.Name} ({v.Type})", "Переменные", "GetVar", new() { ["Name"] = v.Name });
            Add($"Присвоить {v.Name}", "Переменные", "SetVar", new() { ["Name"] = v.Name });
        }
        if (!m.IsStatic && !cls.IsStatic) Add("Этот объект (this)", "Переменные", "This");

        // операции
        foreach (var (op, name) in new[] { ("+", "Сложить"), ("-", "Вычесть"), ("*", "Умножить"), ("/", "Разделить"), ("%", "Остаток от деления") })
            Add($"{name} (int)", "Математика", "Op", new() { ["Op"] = op, ["Type"] = "int" });
        Add("Сложить дробные (double)", "Математика", "Op", new() { ["Op"] = "+", ["Type"] = "double" });
        Add("Умножить дробные (double)", "Математика", "Op", new() { ["Op"] = "*", ["Type"] = "double" });
        Add("Разделить дробные (double)", "Математика", "Op", new() { ["Op"] = "/", ["Type"] = "double" });
        Add("Склеить тексты (+)", "Текст", "Op", new() { ["Op"] = "+", ["Type"] = "string" });
        Add("Собрать текст по шаблону", "Текст", "Format", null, "«Привет, {0}!» — подставит значения вместо {0}, {1}…");

        foreach (var (op, name) in new[] { ("==", "равно"), ("!=", "не равно"), ("<", "меньше"), (">", "больше"), ("<=", "меньше или равно"), (">=", "больше или равно") })
            Add($"Сравнить: {name}", "Логика", "Compare", new() { ["Op"] = op, ["Type"] = "int" });
        Add("Сравнить тексты: равно", "Логика", "Compare", new() { ["Op"] = "==", ["Type"] = "string" });
        Add("И", "Логика", "Logic", new() { ["Op"] = "&&" });
        Add("ИЛИ", "Логика", "Logic", new() { ["Op"] = "||" });
        Add("НЕ", "Логика", "Not");

        foreach (var to in new[] { "string", "int", "double" })
            Add($"Преобразовать в {to}", "Текст", "Convert", new() { ["To"] = to });

        // встроенные функции
        foreach (var b in Builtins)
            Add(b.Title, b.Category, "fn." + b.Id, new(), b.Hint);

        // классы проекта
        foreach (var c in p.Classes)
        {
            if (!c.IsStatic) Add($"Создать объект {c.Name}", "Классы проекта", "NewObject", new() { ["Class"] = c.Name });
            foreach (var mm in c.Methods.Where(x => !x.IsCtor))
                Add($"Вызвать {c.Name}.{mm.Name}", "Классы проекта", "CallMethod", new() { ["Class"] = c.Name, ["Method"] = mm.Name });
            foreach (var f in c.Fields)
            {
                Add($"Взять {c.Name}.{f.Name}", "Классы проекта", "GetField", new() { ["Class"] = c.Name, ["Field"] = f.Name });
                Add($"Записать {c.Name}.{f.Name}", "Классы проекта", "SetField", new() { ["Class"] = c.Name, ["Field"] = f.Name });
            }
        }
        return items;
    }
}
