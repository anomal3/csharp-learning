namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Учебные теги уроков курса Rust. Механика полностью как у TagMapper:
/// заголовок весит больше текста, не более 4 тегов, «общее», если ничего не нашлось.
/// </summary>
public static class RustTagMapper
{
    /// <summary>Словарь тегов — стабильный: добавляем новые, старые не переименовываем.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "старт и cargo", "переменные", "типы данных", "операторы", "условия", "циклы",
        "функции", "замыкания", "кортежи и массивы", "структуры", "enum и паттерны",
        "владение", "ссылки и заимствование", "время жизни", "методы и impl", "трейты",
        "обобщения", "коллекции", "строки", "итераторы", "модули", "ошибки и Result",
        "потоки и синхронизация", "файлы и ввод-вывод", "тесты", "макросы", "unsafe и FFI",
        "новое в Rust",
    };

    static readonly (string Tag, string[] Keywords)[] Rules =
    {
        ("старт и cargo", new[] { "введение", "установка", "rustup", "cargo", "первая программа", "hello world", "rustc", "крейт", "crate", "зависимост" }),
        ("переменные", new[] { "переменн", "констант", "let ", "mut ", "shadowing", "затенени", "неизменяем", "область видимости" }),
        ("типы данных", new[] { "типы данных", "тип данных", "i32", "u8", "f64", "bool", "char", "целые числ", "преобразован", "as i", "приведени" }),
        ("операторы", new[] { "операци", "арифметич", "логическ", "сравнени", "поразряд", "присваиван", "оператор" }),
        ("условия", new[] { "условн", "if ", "else", "if let", "тернарн" }),
        ("циклы", new[] { "цикл", "loop", "while", "for ", "break", "continue", "диапазон", "range" }),
        ("функции", new[] { "функци", "fn ", "параметр", "возвращаемое значение", "аргумент", "рекурсив", "return" }),
        ("замыкания", new[] { "замыкани", "closure", "лямбд", "fnmut", "fnonce", "move |" }),
        ("кортежи и массивы", new[] { "кортеж", "tuple", "массив", "array", "срез", "slice" }),
        ("структуры", new[] { "структур", "struct", "поля структур", "кортежные структур" }),
        ("enum и паттерны", new[] { "enum", "перечислени", "match", "паттерн", "шаблон", "pattern", "option", "some(", "none" }),
        ("владение", new[] { "владени", "ownership", "перемещени", "move ", "copy", "clone", "клонирован", "drop" }),
        ("ссылки и заимствование", new[] { "ссылк", "заимствовани", "borrow", "&mut", "reference", "deref", "указател" }),
        ("время жизни", new[] { "время жизни", "времена жизни", "lifetime", "'static", "'a" }),
        ("методы и impl", new[] { "метод", "impl ", "&self", "ассоциированн", "associated" }),
        ("трейты", new[] { "трейт", "trait", "dyn ", "derive", "default", "display", "debug" }),
        ("обобщения", new[] { "обобщени", "generic", "параметр типа", "where" }),
        ("коллекции", new[] { "коллекц", "vec", "вектор", "hashmap", "hashset", "btreemap", "vecdeque", "словар" }),
        ("строки", new[] { "строк", "string", "&str", "utf-8", "символ", "chars", "format!" }),
        ("итераторы", new[] { "итератор", "iterator", "iter()", ".map(", ".filter(", "collect", "into_iter" }),
        ("модули", new[] { "модул", "module", "mod ", "use ", "pub ", "пространств", "видимост" }),
        ("ошибки и Result", new[] { "ошибк", "result", "panic", "паник", "unwrap", "expect", "оператор ?", "err(", "ok(" }),
        ("потоки и синхронизация", new[] { "поток", "thread", "spawn", "mutex", "мьютекс", "arc", "канал", "channel", "mpsc", "синхронизац", "rwlock", "atomic", "атомарн", "async" }),
        ("файлы и ввод-вывод", new[] { "файл", "ввод", "вывод", "std::fs", "std::io", "stdin", "read_line", "path", "каталог", "bufreader" }),
        ("тесты", new[] { "тест", "#[test]", "assert", "cargo test", "документирован", "бенчмарк" }),
        ("макросы", new[] { "макрос", "macro", "macro_rules", "println!", "vec!", "proc_macro", "атрибут" }),
        ("unsafe и FFI", new[] { "unsafe", "небезопасн", "ffi", "extern", "сырой указател", "raw pointer", "no_mangle", "c-библиотек" }),
        ("новое в Rust", new[] { "нововведен", "что нового", "редакци", "edition", "rust 1." }),
    };

    /// <summary>Теги урока: то, что указано прямо в заголовке, плюс не более 3 заметных по тексту.</summary>
    public static List<string> Of(string title, string body)
    {
        var t = (title ?? "").ToLowerInvariant();
        var b = (body ?? "").ToLowerInvariant();
        var scored = new Dictionary<string, int>();

        foreach (var (tag, keywords) in Rules)
        {
            int score = 0;
            foreach (var kw in keywords)
            {
                if (kw.Length == 0) continue;
                if (t.Contains(kw)) score += 5;
                else if (CountOccurrences(b, kw) >= 3) score += 2;
            }
            if (score > 0) scored[tag] = score;
        }

        if (scored.Count == 0) scored["общее"] = 1;

        var fromTitle = scored.Where(kv => kv.Value >= 5).Select(kv => kv.Key).ToList();
        var rest = scored.Where(kv => kv.Value < 5).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(3).ToList();

        var picked = new List<string>();
        foreach (var tag in fromTitle.Concat(rest))
            if (!picked.Contains(tag)) picked.Add(tag);

        return picked.Take(4).ToList();
    }

    static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    public static string Human(string tag) => tag;
}
