using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CsharpLearning.Core.Runner;

public sealed record IlInstruction(int Offset, string OpCode, string Operand, bool IsFirst);

public sealed record MemoryCell(string Name, string Section, string Type, int SizeBytes, string Value, string Note);

public sealed record MemoryMap(string Explanation, IReadOnlyList<MemoryCell> Cells, int TotalBytes);

public sealed record IlAnalysis(string AssemblyPath, string EntryMethod, IReadOnlyList<IlInstruction> Instructions, MemoryMap Memory, string Notes);

/// <summary>
/// Расширенный режим: берёт реальную сборку примера и показывает
/// 1) CIL-инструкции (то, что JIT превратит в машинный код),
/// 2) карту памяти: сколько байт занимает каждая переменная (стек) и каждый объект (куча).
/// </summary>
public static class IlReader
{
    public static IlAnalysis Analyze(string assemblyPath)
    {
        using var asm = AssemblyDefinition.ReadAssembly(assemblyPath);
        var module = asm.MainModule;

        var entry = module.EntryPoint;
        if (entry is null)
            return new IlAnalysis(assemblyPath, "—", new List<IlInstruction>(),
                new MemoryMap("Сборка без точки входа Main.", new List<MemoryCell>(), 0), "Точка входа не найдена.");

        var body = entry.Body;
        var first = body.Instructions.FirstOrDefault();

        var instructions = new List<IlInstruction>();
        foreach (var i in body.Instructions)
            instructions.Add(new IlInstruction(i.Offset, i.OpCode.Name, Describe(i.Operand, module), i == first));

        var cells = BuildMemoryMap(entry, module);
        int total = cells.Sum(c => c.SizeBytes);

        var expl = new StringBuilder();
        expl.AppendLine("Как выполняется пример:");
        expl.AppendLine("1. initlocals обнуляет все локальные переменные — «мусора» в стеке нет.");
        expl.AppendLine("2. Каждая строка C# становится набором ldloc / stloc / ldarg / call: ldloc = «положи значение», stloc = «запомнить значение», call = «вызвать метод».");
        expl.AppendLine("3. Значимые типы (int, bool, struct) целиком живут в СТЕКЕ кадра метода. Объекты class живут в КУЧЕ, а в стеке лежит только ссылка (8 байт на x64).");
        expl.AppendLine("4. При запуске JIT-компилятор переводит эти CIL-инструкции в машинный код процессора — поэтому один и тот же exe работает на разном железе.");

        var notes = new StringBuilder();
        notes.AppendLine($"Точка входа: {entry.FullName}");
        notes.AppendLine($"Локальных переменных (locals): {body.Variables.Count}; параметров: {entry.Parameters.Count}; максимальная глубина стека (maxstack): {body.MaxStackSize}");
        notes.AppendLine($"Инструкций CIL: {instructions.Count}");

        return new IlAnalysis(assemblyPath, entry.FullName, instructions, new MemoryMap(expl.ToString(), cells, total), notes.ToString());
    }

    static string Describe(object? operand, ModuleDefinition module)
    {
        switch (operand)
        {
            case null: return "";
            case VariableDefinition v: return $"loc_{v.Index} : {ShortType(v.VariableType, module)}";
            case ParameterDefinition p: return $"arg{p.Index} ({p.Name}) : {ShortType(p.ParameterType, module)}";
            case MethodReference m: return m.FullName;
            case TypeReference t: return ShortType(t, module);
            case FieldReference f: return f.FullName;
            case Instruction target: return $"→ IL_{target.Offset}";
            case string s: return $"\"{s}\"";
            default: return operand.ToString() ?? "";
        }
    }

    static string ShortType(TypeReference t, ModuleDefinition module)
    {
        var name = t.FullName;
        return name switch
        {
            "System.Int32" => "int",
            "System.String" => "string",
            "System.Boolean" => "bool",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Object" => "object",
            _ when name.StartsWith("System.") => name[7..],
            _ => name,
        };
    }

    static bool IsReferenceType(TypeReference t, ModuleDefinition module)
    {
        if (t is ArrayType) return true;
        if (t.FullName == "System.String") return true;
        try
        {
            var def = t.Resolve();
            return def is not null && !def.IsValueType;
        }
        catch { return !t.IsValueType; }
    }

    static List<MemoryCell> BuildMemoryMap(MethodDefinition entry, ModuleDefinition module)
    {
        var cells = new List<MemoryCell>();

        foreach (var p in entry.Parameters)
            cells.Add(new MemoryCell($"arg{p.Index} {p.Name}", "стек вызова (параметры)", ShortType(p.ParameterType, module),
                SizeOf(p.ParameterType), "—", "Параметр метода: для ссылочного типа сюда кладётся указатель 8 байт."));

        foreach (var v in entry.Body.Variables)
        {
            var t = v.VariableType;
            bool isRef = IsReferenceType(t, module);
            cells.Add(new MemoryCell($"loc_{v.Index}", isRef ? "стек: ссылка (объект в куче)" : "стек кадра (значение целиком)",
                ShortType(t, module), isRef ? 8 : SizeOf(t), isRef ? "null до newobj" : DefaultOf(t),
                isRef ? "Сам объект лежит в куче; в стеке только адрес." : "Все байты значения живут в стеке кадра метода."));
        }

        var heap = new List<MemoryCell>();
        foreach (var i in entry.Body.Instructions)
        {
            if (i.OpCode.Code == Code.Newobj && i.Operand is MethodReference mr && mr.DeclaringType is TypeReference tr)
            {
                heap.Add(new MemoryCell(ShortType(tr, module), "куча (heap)", ShortType(tr, module), HeapSize(tr),
                    "адрес объекта", "newobj: выделен блок в куче; ссылка кладётся на стек следующей инструкцией."));
            }
            else if (i.OpCode.Code == Code.Initobj && i.Operand is TypeReference str)
            {
                heap.Add(new MemoryCell(ShortType(str, module), "значимый тип (без объекта в куче)", ShortType(str, module), SizeOf(str),
                    "обнулено", "initobj: структура инициализируется на месте — отдельного объекта в куче нет."));
            }
            else if (i.OpCode.Name.StartsWith("ldc"))
            {
                heap.Add(new MemoryCell("константа", "CIL-поток сборки (статический код)", i.Operand?.GetType().Name ?? "", 4,
                    i.Operand?.ToString() ?? "", "ldc.*: значение зашито прямо в код инструкции, отдельной переменной нет."));
            }
        }

        cells.AddRange(heap.Take(30));
        return cells;
    }

    static int SizeOf(TypeReference t)
    {
        switch (t.FullName)
        {
            case "System.Boolean":
            case "System.SByte":
            case "System.Byte": return 1;
            case "System.Int16":
            case "System.UInt16":
            case "System.Char": return 2;
            case "System.Int32":
            case "System.UInt32":
            case "System.Single": return 4;
            case "System.Int64":
            case "System.UInt64":
            case "System.Double": return 8;
            case "System.Decimal": return 16;
        }

        if (t is ArrayType || t.FullName == "System.String") return 8;

        try
        {
            var def = t.Resolve();
            if (def is null) return 8;
            if (!def.IsValueType) return 8;
            int sum = 0;
            foreach (var f in def.Fields.Where(f => !f.IsStatic)) sum += SizeOf(f.FieldType);
            return Math.Max(sum, 1);
        }
        catch { return 8; }
    }

    static int HeapSize(TypeReference t)
    {
        try
        {
            var def = t.Resolve();
            if (def is null) return 32;
            int sum = 16; // x64: указатель на method table + блок синхронизации
            foreach (var f in def.Fields.Where(f => !f.IsStatic)) sum += SizeOf(f.FieldType);
            return (sum + 7) / 8 * 8; // выравнивание по 8 байт
        }
        catch { return 32; }
    }

    static string DefaultOf(TypeReference t) => t.FullName switch
    {
        "System.Boolean" => "False",
        "System.String" => "null",
        _ => "0",
    };
}
