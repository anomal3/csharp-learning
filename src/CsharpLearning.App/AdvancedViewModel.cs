using System.IO;
using CsharpLearning.App.Services;
using CsharpLearning.App.View;
using CsharpLearning.Core.Runner;

namespace CsharpLearning.App;

public sealed record ILRow(string Op, string Operand, string PlainLanguage);

public sealed record TypeRow(string Type, string Size, string Range, string Purpose);

public sealed record MemoryRow(string Name, string Section, string Type, string Size, string Value, string Why);

/// <summary>
/// Расширенный режим: показывает, как пример становится сборкой, а сборка — машинным кодом и памятью.
/// 1) как компилятор упаковал «урочный» код в Main;
/// 2) реальный вывод программы;
/// 3) CIL-инструкции (это ещё не машинный код, а его заготовка, которую JIT превратит в машинный);
/// 4) карта памяти: сколько байт и где занимает каждая переменная и каждый объект.
/// </summary>
public sealed class AdvancedViewModel : NotifyBase
{
    readonly LearningState _state;

    public AdvancedViewModel(LearningState state) => _state = state;

    string _source = "";
    public string Source { get => _source; private set => Set(ref _source, value); }

    string _output = "";
    public string Output { get => _output; private set => Set(ref _output, value); }

    string _errors = "";
    public string Errors { get => _errors; private set => Set(ref _errors, value); }

    public bool HasErrors => !string.IsNullOrWhiteSpace(Errors);

    string _notes = "";
    public string Notes { get => _notes; private set => Set(ref _notes, value); }

    string _memoryExplanation = "";
    public string MemoryExplanation { get => _memoryExplanation; private set => Set(ref _memoryExplanation, value); }

    public List<ILRow> Instructions { get; } = new();
    public List<MemoryRow> Memory { get; } = new();

    public bool HasAnalysis => Instructions.Count > 0 || Memory.Count > 0;

    /// <summary>Справка «сколько байт занимают типы, встреченные в этом примере».</summary>
    public List<TypeRow> TypeRows { get; } = new();
    public bool HasTypeRows => TypeRows.Count > 0;
    public string TypeTitle => "Сколько памяти занимают типы из этого примера";

    static readonly (string name, string size, string range, string purpose)[] TypeFacts =
    {
        ("byte", "1 Б", "0 … 255", "цвет пикселя, байт файла"),
        ("sbyte", "1 Б", "−128 … 127", "малые числа со знаком"),
        ("short", "2 Б", "−32 768 … 32 767", "экономия памяти в огромных массивах"),
        ("ushort", "2 Б", "0 … 65 535", "порты, малые счётчики"),
        ("int", "4 Б", "≈ ±2,1 млрд", "целые по умолчанию: счётчики, индексы, возраст"),
        ("uint", "4 Б", "0 … ≈ 4,29 млрд", "числа без знака, битовые маски"),
        ("long", "8 Б", "≈ ±9,2·10¹⁸", "большие числа: id в базе, размер файла, время в мс"),
        ("ulong", "8 Б", "0 … ≈ 1,8·10¹⁹", "очень большие положительные числа"),
        ("float", "4 Б", "~7 значащих цифр", "графика, игры"),
        ("double", "8 Б", "~15 значащих цифр", "дробные по умолчанию: измерения, наука"),
        ("decimal", "16 Б", "28 значащих цифр", "деньги и всё, где важна точность"),
        ("bool", "1 Б", "true / false", "флаги и условия"),
        ("char", "2 Б", "один символ UTF-16", "разбор текста по буквам"),
        ("string", "8 Б ссылка + 2 Б на символ в куче", "текст любой длины", "имена, сообщения, любые тексты"),
        ("object", "8 Б ссылка + объект в куче", "любой тип", "когда тип заранее неизвестен"),
        ("DateTime", "8 Б", "годы 0001 … 9999", "момент времени"),
        ("Guid", "16 Б", "128 бит", "уникальный идентификатор"),
    };

    void FillTypeRows(string source)
    {
        TypeRows.Clear();
        foreach (var f in TypeFacts)
            if (System.Text.RegularExpressions.Regex.IsMatch(source, @"(?<![\w.])" + f.name + @"(?![\w])"))
                TypeRows.Add(new TypeRow(f.name, f.size, f.range, f.purpose));

        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"\w+\[\]"))
            TypeRows.Add(new TypeRow("массив T[]", "8 Б ссылка + 24 Б заголовок + элементы", "размер задаётся при создании", "несколько однотипных значений подряд"));
    }

    string _status = "Нажми «Разобрать код» — покажу, что из него получается после компиляции.";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public string SourceTitle => "Исходник урока → как его упаковал компилятор (точка входа Main)";
    public string ILTitle => "CIL-код (промежуточный язык .NET). JIT-компилятор превратит эти строки в машинный код CPU при запуске.";
    public string MemoryTitle => "Карта памяти запуска";

    int _totalBytes;
    public int TotalBytes { get => _totalBytes; private set => Set(ref _totalBytes, value); }

    public string TotalBytesText => TotalBytes > 0 ? $"{TotalBytes} байт (стек кадра + куча, без заголовков JIT)" : "";

    public async Task AnalyzeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            Status = "Нет кода для разбора: открой урок с примером.";
            Instructions.Clear(); Memory.Clear(); TypeRows.Clear();
            Raise(nameof(HasTypeRows));
            Raise(nameof(HasAnalysis));
            return;
        }

        Status = "Компилирую…";
        Instructions.Clear();
        Memory.Clear();
        TypeRows.Clear();
        Errors = "";
        Output = "";

        string workDir = Path.Combine(Paths.RunWork, Guid.NewGuid().ToString("N"));

        var compile = await Task.Run(() =>
        {
            try { return IlWorkbench.Compile(code, workDir, "advanced"); }
            catch (Exception ex) { return new CompileResult(false, null, "Сбой компиляции: " + ex.Message, code, IlWorkbench.ConsoleFramework); }
        });

        Source = IlWorkbench.DisplaySource(compile.Source);

        if (!compile.Success)
        {
            Errors = compile.Diagnostics;
            Status = "Компилятор отказался принять пример. Так же откажется и Visual Studio.";
            Raise(nameof(HasErrors));
            Raise(nameof(HasAnalysis));
            Raise(nameof(Source));
            return;
        }

        if (!string.IsNullOrWhiteSpace(compile.Diagnostics)) Errors = compile.Diagnostics;

        Status = "Запускаю собранную программу в отдельном процессе…";
        var run = await Task.Run(() => SandboxRunner.Run(compile.AssemblyPath!, 8000, workDir));

        Output = run.Output.Trim().Length > 0 ? run.Output.Trim() : "(ничего не выведено)";
        if (run.Error.Trim().Length > 0) Errors = (Errors + "\n" + run.Error).Trim();
        if (run.TimedOut) Errors = (Errors + "\n" + "Программа не завершилась — похоже на бесконечный цикл.").Trim();

        var analysis = await Task.Run(() => IlReader.Analyze(compile.AssemblyPath!));

        foreach (var i in analysis.Instructions)
            Instructions.Add(new ILRow("IL_" + i.Offset, i.OpCode + (i.Operand.Length > 0 ? " " + i.Operand : ""), Plain(i.OpCode, i.Operand)));

        foreach (var c in analysis.Memory.Cells)
            Memory.Add(new MemoryRow(c.Name, c.Section, c.Type, c.SizeBytes + " Б", c.Value, c.Note));

        FillTypeRows(Source);
        MemoryExplanation = analysis.Memory.Explanation;
        Notes = analysis.Notes;
        TotalBytes = analysis.Memory.TotalBytes;

        Status = $"Разобрано: {Instructions.Count} CIL-инструкций, {Memory.Count} ячеек памяти, {TotalBytes} байт. " +
                 (run.ElapsedMs < 200 ? "Выполнение заняло меньше кадра экрана." : $"Выполнение: {run.ElapsedMs:F0} мс.");

        TryCleanup(workDir);

        Raise(nameof(HasAnalysis));
        Raise(nameof(TypeRows));
        Raise(nameof(HasTypeRows));
        Raise(nameof(Instructions));
        Raise(nameof(Memory));
        Raise(nameof(HasErrors));
        Raise(nameof(TotalBytesText));
        Raise(nameof(MemoryExplanation));
        Raise(nameof(Notes));
        Raise(nameof(Output));
    }

    static string Plain(string op, string operand) => op switch
    {
        "nop" => "ничего не делать",
        "initlocals" => "обнулить все локальные переменные кадра",
        "ldarg.0" => "положить на стек первый аргумент",
        "ldloc" => "положить на стек значение локальной переменной",
        "stloc" => "забрать со стека и сохранить в локальную переменную",
        "ldc" => "положить на стек константу, зашитую в коде",
        "ldstr" => "положить на стек строку",
        "call" => "вызвать метод: " + operand,
        "callvirt" => "вызвать виртуальный метод: " + operand + " (ищет переопределение в потомке)",
        "newobj" => "создать объект в КУЧЕ и положить его адрес на стек",
        "ldfld" => "прочитать поле объекта",
        "stfld" => "записать в поле объекта",
        "ldloca" => "положить на стек АДРЕС локальной переменной (для ref)",
        "ret" => "вернуть управление вызывающему коду",
        "ldtoken" => "положить токен типа (используется рефлексией)",
        "br" => "переход (перепрыгнуть на другую инструкцию)",
        "beq" => "перейти, если значения равны",
        "blt" => "перейти, если меньше",
        "bge" => "перейти, если больше или равно",
        "dup" => "продублировать верхнее значение на стеке",
        "pop" => "выбросить верхнее значение",
        "box" => "упаковать значимый тип в объект (переместить из стека в кучу)",
        "unbox" => "достать значение из упакованного объекта",
        "castclass" => "привести тип по ссылке",
        _ => "",
    };

    static void TryCleanup(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { /* следующее удаление разберётся */ }
    }
}
