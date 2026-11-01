using System.Text.Json;
using System.Text.Json.Serialization;

namespace CsharpLearning.Core.Graph;

/// <summary>
/// Проект визуального программирования на C#: набор классов, у каждого — поля и методы,
/// а тело каждого метода — граф из нод. Из проекта генерируется обычный исходный код C#.
/// </summary>
public sealed class GraphProject
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Мой проект";
    public List<ClassDef> Classes { get; set; } = new();

    /// <summary>Точка входа: static-метод без параметров. По умолчанию Program.Main.</summary>
    public string EntryClass { get; set; } = "Program";
    public string EntryMethod { get; set; } = "Main";

    public ClassDef? FindClass(string? name) => Classes.FirstOrDefault(c => c.Name == name);

    public static GraphProject CreateEmpty()
    {
        var p = new GraphProject();
        var program = new ClassDef { Name = "Program", IsStatic = true };
        var main = new MethodDef { Name = "Main", IsStatic = true };
        main.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 40, Y = 80 });
        program.Methods.Add(main);
        p.Classes.Add(program);
        return p;
    }
}

public sealed class ClassDef
{
    public string Name { get; set; } = "Класс";
    public string? BaseClass { get; set; }
    public bool IsStatic { get; set; }
    public List<FieldDef> Fields { get; set; } = new();
    public List<MethodDef> Methods { get; set; } = new();

    public MethodDef? FindMethod(string? name) => Methods.FirstOrDefault(m => m.Name == name);
    public FieldDef? FindField(string? name) => Fields.FirstOrDefault(f => f.Name == name);
    public MethodDef? Constructor => Methods.FirstOrDefault(m => m.Name == MethodDef.CtorName);
}

public sealed class FieldDef
{
    public string Name { get; set; } = "поле";
    public string Type { get; set; } = "int";
    /// <summary>Начальное значение — выражение C# как есть («100», «\"Привет\"»); пусто — без значения.</summary>
    public string Initial { get; set; } = "";
    public bool IsStatic { get; set; }
    /// <summary>Автосвойство { get; set; } вместо обычного поля.</summary>
    public bool IsProperty { get; set; }
}

public sealed class ParamDef
{
    public string Name { get; set; } = "x";
    public string Type { get; set; } = "int";
}

/// <summary>Локальная переменная метода: объявляется в начале метода, а ноды «Значение» и «Присвоить» работают с ней.</summary>
public sealed class LocalDef
{
    public string Name { get; set; } = "x";
    public string Type { get; set; } = "int";
    /// <summary>Начальное значение (пусто — значение по умолчанию для типа).</summary>
    public string Initial { get; set; } = "";
}

public sealed class MethodDef
{
    /// <summary>Служебное имя конструктора в списке методов класса.</summary>
    public const string CtorName = ".ctor";

    public string Name { get; set; } = "Метод";
    public string ReturnType { get; set; } = "void";
    public bool IsStatic { get; set; }
    /// <summary>"", "virtual" или "override".</summary>
    public string Modifier { get; set; } = "";
    public List<ParamDef> Params { get; set; } = new();
    public List<LocalDef> Locals { get; set; } = new();
    public GraphData Graph { get; set; } = new();

    [JsonIgnore] public bool IsCtor => Name == CtorName;
    [JsonIgnore] public bool IsVoid => ReturnType is "void" or "";
}

public sealed class GraphData
{
    public List<NodeData> Nodes { get; set; } = new();
    public List<WireData> Wires { get; set; } = new();

    public NodeData? Find(string? id) => Nodes.FirstOrDefault(n => n.Id == id);
    public NodeData? Entry => Nodes.FirstOrDefault(n => n.Kind == "Entry");
}

public sealed class NodeData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Kind { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Props { get; set; } = new();

    public string Prop(string name, string fallback = "") => Props.TryGetValue(name, out var v) ? v : fallback;
}

/// <summary>Связь «выход одной ноды → вход другой». Для потока выполнения и для данных.</summary>
public sealed class WireData
{
    public string FromNode { get; set; } = "";
    public string FromPin { get; set; } = "";
    public string ToNode { get; set; } = "";
    public string ToPin { get; set; } = "";
}

public static class GraphJson
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Save(GraphProject p) => JsonSerializer.Serialize(p, Options);

    public static GraphProject Load(string json) =>
        JsonSerializer.Deserialize<GraphProject>(json, Options) ?? GraphProject.CreateEmpty();

    /// <summary>Глубокая копия проекта (для истории отмены и копирования).</summary>
    public static GraphProject Clone(GraphProject p) => Load(Save(p));
}
