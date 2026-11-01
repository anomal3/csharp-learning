namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Учебные теги урока. Нужны для аналитики: «где именно проседаешь» считается
/// не по 195 урокам, а по ~24 темам. Теги ставит краулер, их же показывает приложение.
/// </summary>
public static class TagMapper
{
    /// <summary>Словарь тегов — стабильный: добавляем новые, старые не переименовываем.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "старт и тулчейн", "переменные", "типы данных", "операторы", "условия", "циклы",
        "массивы и индексы", "методы и return", "программа и Main", "классы и объекты",
        "конструкторы", "свойства", "статические члены", "ООП и наследование", "интерфейсы",
        "обобщения", "делегаты и лямбды", "события", "исключения", "коллекции",
        "строки", "дата и время", "файлы и потоки", "json и xml", "многопоточность",
        "async и задачи", "linq", "рефлексия и атрибуты", "память и GC", "процессы и публикация",
        "новое в C#",
    };

    static readonly (string Tag, string[] Keywords)[] Rules =
    {
        ("старт и тулчейн", new[] { "введение", "установка", "visual studio", "dotnet cli", "первая программа", "wsl", "dotnetup", "тулчейн", "net cli", "программа верхнего уровня" }),
        ("переменные", new[] { "переменн", "констант", "литерал", "var ", "область видимости", "контекст переменных", "инициализац" }),
        ("типы данных", new[] { "типы данных", "преобразован", "значимые типы", "ссылочные типы", "null", "bool", "convert", "псевдоним", "ref-структур" }),
        ("операторы", new[] { "операци", "арифметич", "поразряд", "присваиван", "условные выражения", "ternary", "тернарн", "операторов" }),
        ("условия", new[] { "if..else", "if", "switch", "условн" }),
        ("циклы", new[] { "цикл", "for", "while", "foreach", "итерац", "итератор", "yield" }),
        ("массивы и индексы", new[] { "массив", "индекс", "диапазон", "span", "индексатор", "кортеж", "задачи с массивами" }),
        ("методы и return", new[] { "метод", "param", "return", "возврат", "рекурсив", "локальные функции", "перегрузк", "параметр" }),
        ("программа и Main", new[] { "метод main", "класс program", "программы верхнего уровня", "структура программы", "пространств имен", "namespaces", "библиотеки классов", "nuget" }),
        ("классы и объекты", new[] { "класс", "объект", "экземпляр", "структур", "record", "records", "анонимные типы", "частичные класс", "копирование объектов", "модификатор", "field" }),
        ("конструкторы", new[] { "конструктор", "инициализатор", "деконструктор" }),
        ("свойства", new[] { "свойств", "паттерн свойств" }),
        ("статические члены", new[] { "static", "статическ" }),
        ("ООП и наследование", new[] { "наследов", "виртуальн", "override", "abstract", "абстрактн", "система.object", "system.object", "переопределен", "скрытие методов", "расширения типов", "полиморф" }),
        ("интерфейсы", new[] { "интерфейс", "icomparable", "icollection", "обобщени" }),
        ("обобщения", new[] { "обобщени", "generic", "ковариант", "контравариант" }),
        ("делегаты и лямбды", new[] { "делегат", "лямбд", "lambda", "action", "func", "predicate", "замыкан", "анонимные методы", "closure" }),
        ("события", new[] { "событи", "event" }),
        ("исключения", new[] { "исключен", "try", "catch", "throw", "exception", "finally" }),
        ("коллекции", new[] { "коллекц", "list", "queue", "stack", "dictionary", "observablecollection", "ienumerable", "ienumerator", "linkedlist", "сортировка объектов" }),
        ("строки", new[] { "строк", "string", "stringbuilder", "регулярн", "интерполяц", "форматирован" }),
        ("дата и время", new[] { "дат", "врем", "datetime", "dateonly", "timeonly", "timespan" }),
        ("файлы и потоки", new[] { "файл", "каталог", "filestream", "streamreader", "streamwriter", "binarywriter", "binaryreader", "архивац", "сжатие", "работа с файловой системой", "using " }),
        ("json и xml", new[] { "json", "xml", " сериализац", "xpath", "xmlserializer", "jsonserializer" }),
        ("многопоточность", new[] { "поток", "thread", "синхронизац", "монитор", "мьютекс", "семафор", "lock", "autore seteevent", "автосброс" }),
        ("async и задачи", new[] { "async", "await", "task", "параллельн", "tpl", "cancel", "асинхрон" }),
        ("linq", new[] { "linq", "запрос", "фильтрац", "группировк", "соединен", "агрегатн", "проецирован", "проекция" }),
        ("рефлексия и атрибуты", new[] { "рефлекси", "атрибут", "dynamic", "expando", "ironpython", "позднее связывание", "загрузка сборок" }),
        ("память и GC", new[] { "сборк мусора", "gc", "dispose", "указател", "финализир", "память", "lazy" }),
        ("процессы и публикация", new[] { "процесс", "домен", "публикац", "native aot", "assemblyloadcontext", "приложения на основе файлов" }),
        ("математика", Array.Empty<string>()),
        ("новое в C#", new[] { "нововведен", "c# 1", "что нового" }),
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
