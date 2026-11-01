using System.Text;
using CsharpLearning.Core.Model;

namespace CsharpLearning.Core.Pedagogy;

/// <summary>Один сгенерированный учебный пример.</summary>
public sealed class GeneratedExample
{
    public string Concept { get; init; } = "";
    public QuizFormat Format { get; init; }
    public string Prompt { get; init; } = "";
    public string Code { get; init; } = "";
    /// <summary>Для GUI-примеров: консольный каркас, чей вывод совпадает с ответом.</summary>
    public string? HarnessCode { get; init; }
    public List<string> Options { get; init; } = new();
    public string CorrectAnswer { get; init; } = "";
    public string Explanation { get; init; } = "";
    public bool NeedsVerification { get; init; }
    public bool IsGui { get; init; }
}

/// <summary>
/// Генератор примеров и вопросов. Каждый пример привязан к концепции урока,
/// поэтому вопросы не абстрактные, а «из этого же урока».
/// Эталонный ответ для PredictOutput вычисляется реальным запуском кода (Roslyn),
/// поэтому ответ не может быть ошибочным.
/// </summary>
public static class ExampleGenerator
{
    public static List<GeneratedExample> For(string concept)
    {
        return concept switch
        {
            "Класс" => ClassExamples(),
            "Метод" => MethodExamples(),
            "Метод и return" => ReturnExamples(),
            "Параметр по умолчанию" => DefaultParamExamples(),
            "Переменная" => VariableExamples(),
            "Тип данных" => TypeExamples(),
            "Условие if/else" => IfExamples(),
            "Цикл" => LoopExamples(),
            "Массив" => ArrayExamples(),
            "Строка и интерполяция" => StringExamples(),
            "Наследование" => InheritanceExamples(),
            "Интерфейс" => InterfaceExamples(),
            "Делегат" => DelegateExamples(),
            "Исключение" => ExceptionExamples(),
            "Свойство" => PropertyExamples(),
            "Конструктор" => ConstructorExamples(),
            "Структура и класс" => StructClassExamples(),
            "Статический член" => StaticExamples(),
            "Словарь и коллекции" => CollectionExamples(),
            "LINQ" => LinqExamples(),
            "Многопоточность" => ThreadingExamples(),
            "Память и сборка мусора" => MemoryExamples(),
            "Файлы и потоки" => FileExamples(),
            _ => GenericExamples(concept),
        };
    }

    static GeneratedExample Predict(string concept, string prompt, string code, string explanation, string? harness = null)
        => new()
        {
            Concept = concept,
            Format = QuizFormat.PredictOutput,
            Prompt = prompt,
            Code = code.Trim(),
            HarnessCode = harness?.Trim(),
            Explanation = explanation,
            NeedsVerification = true,
        };

    // ---------------- Класс ----------------
    static List<GeneratedExample> ClassExamples() => new()
    {
        Predict("Класс", "Что выведет программа?",
            """
            class Dog
            {
                public string Name = "Барос";
                public void Voice() => Console.WriteLine($"{Name}: Гав!");
            }

            var a = new Dog();
            var b = new Dog { Name = "Рекс" };
            a.Voice();
            b.Voice();
            Console.WriteLine(a.Name == b.Name);
            """,
            "new Dog — два независимых объекта. У первого имя из поля по умолчанию, у второго — из инициализатора. Переменные a и b указывают на разные объекты, поэтому имена не равны."),

        Predict("Класс", "Сколько объектов Dog создано и что выведет последняя строка?",
            """
            class Dog
            {
                public static int Count;
                public Dog() => Count++;
            }

            var first = new Dog();
            var second = new Dog();
            var third = new Dog();
            Console.WriteLine(Dog.Count);
            """,
            "static Count — одна общая «конура» на весь класс: каждый конструктор увеличивает её на 1. Итого 3."),

        new()
        {
            Concept = "Класс", Format = QuizFormat.MultipleChoice,
            Prompt = "Что из этого — «чертёж» (класс), а что — «построенный дом» (объект)?",
            Code = "class Dog { }\nvar baros = new Dog();",
            Options = new() { "Dog — класс, baros — объект", "baros — класс, Dog — объект", "оба — объекты", "оба — классы" },
            CorrectAnswer = "Dog — класс, baros — объект",
            Explanation = "class описывает породу (чертёж). new Dog() строит конкретный экземпляр.",
        },
    };

    // ---------------- Метод ----------------
    static List<GeneratedExample> MethodExamples() => new()
    {
        Predict("Метод", "Что выведет программа?",
            """
            string MyString(int a)
            {
                return a + " + 2";
            }

            Console.WriteLine(MyString(10));
            Console.WriteLine(MyString(5 + 3));
            """,
            "a + \" + 2\" — склейка: число превращается в текст и присоединяется к строке. Вторая строка: сначала считается 5+3=8, потом передаётся в метод."),

        Predict("Метод", "Что выведет программа?",
            """
            void ByValue(int x) => x *= 2;
            void ByRef(ref int y) => y *= 2;

            int a = 5;
            int b = 5;
            ByValue(a);
            ByRef(ref b);
            Console.WriteLine(a);
            Console.WriteLine(b);
            """,
            "Без ref метод получает КОПИЮ a: изменение видно только внутри, a остаётся 5. С ref y — это сама переменная b, поэтому b становится 10."),

        Predict("Метод", "Что выведет программа?",
            """
            int Sum(params int[] numbers)
            {
                int total = 0;
                foreach (var n in numbers) total += n;
                return total;
            }

            Console.WriteLine(Sum(1, 2, 3));
            Console.WriteLine(Sum());
            """,
            "params собирает все аргументы в массив. Sum(1,2,3) → 6, Sum() → пустой массив → 0."),
    };

    // ---------------- return / параметры ----------------
    static List<GeneratedExample> ReturnExamples() => new()
    {
        Predict("Метод и return", "Что выведет программа?",
            """
            int Try(int a)
            {
                if (a > 5) return a * 2;
                return a;
            }

            Console.WriteLine(Try(7));
            Console.WriteLine(Try(3));
            """,
            "return немедленно заканчивает метод: после return a*2 строка return a; уже не выполняется."),

        Predict("Метод и return", "Что выведет программа?",
            """
            int Counter()
            {
                int c = 0;
                while (c < 4)
                {
                    c++;
                    if (c == 2) return c;
                }
                return -1;
            }

            Console.WriteLine(Counter());
            """,
            "На второй итерации return 2 выходит из метода и из цикла сразу, поэтому -1 никогда не возвращается."),
    };

    static List<GeneratedExample> DefaultParamExamples() => new()
    {
        Predict("Параметр по умолчанию", "Что выведет программа?",
            """
            string MyString(int a = 0) => $"{a} + 2";

            Console.WriteLine(MyString());
            Console.WriteLine(MyString(10));
            """,
            "MyString() берёт значение по умолчанию 0 → «0 + 2». MyString(10) → «10 + 2». Это строки: плюс внутри кавычек — текст, а не сложение."),

        Predict("Параметр по умолчанию", "Что выведет программа?",
            """
            void Greet(string name = "гость", int times = 1)
            {
                for (int i = 0; i < times; i++) Console.WriteLine($"Привет, {name}!");
            }

            Greet();
            Greet(times: 2);
            Greet("Барос", 1);
            """,
            "Именованный вызов Greet(times: 2) пропускает name (берётся «гость») и повторяет вывод дважды."),
    };

    // ---------------- Переменные и типы ----------------
    static List<GeneratedExample> VariableExamples() => new()
    {
        Predict("Переменная", "Что выведет программа?",
            """
            int a = 0;
            a = a + 2;
            int b = a;
            b = 100;
            Console.WriteLine(a);
            Console.WriteLine(b);
            """,
            "b = a копирует ЗНАЧЕНИЕ (int — значимый тип). Дальнейшая правка b не трогает a: 2 и 100."),

        Predict("Переменная", "Что выведет программа?",
            """
            var x = 10;
            var y = 10.0;
            Console.WriteLine(x / 4);
            Console.WriteLine(y / 4);
            """,
            "var выводит тип из правой части: x — int, поэтому 10/4 = 2 (целочисленное деление). y — double → 2.5."),
    };

    static List<GeneratedExample> TypeExamples() => new()
    {
        Predict("Тип данных", "Что выведет программа?",
            """
            Console.WriteLine(0 + 2);
            Console.WriteLine("0" + 2);
            Console.WriteLine('7' + 3);
            """,
            "0+2 — арифметика → 2. \"0\"+2 — склейка строк → 02. '7'+3 — char складывается как коды: 55+3=58."),

        Predict("Тип данных", "Что выведет программа?",
            """
            int i = (int)7.9;
            double d = 5 / 2;
            Console.WriteLine(i);
            Console.WriteLine(d);
            """,
            "(int)7.9 отбрасывает дробь → 7. 5/2 считается как целочисленное (2) и только потом превращается в double → 2."),
    };

    // ---------------- Условия ----------------
    static List<GeneratedExample> IfExamples() => new()
    {
        Predict("Условие if/else", "Что выведет программа?",
            """
            int a = 7;
            if (a > 10) Console.WriteLine("большое");
            else if (a > 5) Console.WriteLine("среднее");
            else Console.WriteLine("маленькое");

            string t = a > 5 ? "да" : "нет";
            Console.WriteLine(t);
            """,
            "Первое условие ложно, второе истинно → «среднее». Тернарный оператор выбирает одно из двух значений."),

        Predict("Условие if/else", "Что выведет программа?",
            """
            bool canWalk = true;
            bool hasLeash = false;

            if (canWalk && hasLeash) Console.WriteLine("гуляем");
            if (canWalk || hasLeash) Console.WriteLine("гуляем можно");
            if (canWalk && !hasLeash) Console.WriteLine("без поводка");
            """,
            "&& требует оба условия; || достаточно одного; ! инвертирует. Итог: вторая и третья строки."),
    };

    // ---------------- Циклы ----------------
    static List<GeneratedExample> LoopExamples() => new()
    {
        Predict("Цикл", "Что выведет программа?",
            """
            int a = 0;
            while (a < 3)
            {
                Console.WriteLine($"while: {a}");

                a++;
            }

            int b = 0;
            do
            {
                Console.WriteLine($"do: {b}");
                b++;
            }
            while (b < 0);
            """,
            "while проверяет ДО шага → 0,1,2. do..while проверяет ПОСЛЕ, поэтому тело выполнится хотя бы один раз даже при ложном условии → «do: 0»."),

        Predict("Цикл", "Что выведет программа?",
            """
            for (int i = 0; i < 3; i++)
            {
                if (i == 1) continue;
                if (i == 2) break;
                Console.WriteLine(i);
            }
            """,
            "continue пропускает итерацию (1 не печатается), break полностью выходит из цикла на i==2 → печатается только 0."),

        Predict("Цикл", "Сколько раз выполнится тело?",
            """
            int count = 0;
            for (int i = 10; i > 0; i--)
            {
                count++;
                if (count == 4) break;
            }
            Console.WriteLine(count);
            """,
            "break срабатывает на 4-м шаге → 4."),

        new()
        {
            Concept = "Цикл", Format = QuizFormat.MultipleChoice,
            Prompt = "Чем do..while принципиально отличается от while?",
            Code = "while (false) { Console.WriteLine(1); }\ndo { Console.WriteLine(2); } while (false);",
            Options = new() { "do..while выполнится один раз, while — ни разу", "оба не выполнятся", "оба выполнятся один раз", "while выполнится один раз" },
            CorrectAnswer = "do..while выполнится один раз, while — ни разу",
            Explanation = "do..while сначала делает, потом проверяет — минимум один шаг гарантирован.",
        },

    };

    // ---------------- Массивы ----------------
    static List<GeneratedExample> ArrayExamples() => new()
    {
        Predict("Массив", "Что выведет программа?",
            """
            int[] a = { 10, 20, 30 };
            Console.WriteLine(a[0] + a[2]);
            Console.WriteLine(a.Length);
            Console.WriteLine(a[^1]);
            """,
            "Индексация с нуля: a[0]=10, a[2]=30 → 40. Length = 3. a[^1] — последний элемент (диапазон с конца)."),

        Predict("Массив", "Что выведет программа?",
            """
            int[] a = new int[3];
            for (int i = 0; i <= a.Length; i++)
            {
                if (i == a.Length) { Console.WriteLine("выход за границу"); break; }
            }
            Console.WriteLine(a[0]);
            """,
            "new int[3] заполнен нулями по умолчанию. Индекс 3 невалидный — записывать туда нельзя, a[0] == 0."),
    };

    // ---------------- Строки ----------------
    static List<GeneratedExample> StringExamples() => new()
    {
        Predict("Строка и интерполяция", "Что выведет программа?",
            """
            string MyString(int a = 0) => $"{a} + 2";
            Console.WriteLine(MyString(10));
            Console.WriteLine(MyString(10).Length);
            """,
            "$\"{a} + 2\" — текст с подстановкой: получается «10 + 2», длина 7 символов. Это НЕ 12."),

        Predict("Строка и интерполяция", "Что выведет программа?",
            """
            var s = new System.Text.StringBuilder("кот");
            s.Append("ёж");
            s.Insert(0, "и ");
            Console.WriteLine(s);
            Console.WriteLine("кот" == "кот");
            Console.WriteLine(object.ReferenceEquals("кот", "кот"));
            """,
            "StringBuilder меняет себя на месте (string сам неизменяем). Литералы с одинаковым текстом интернируются — ReferenceEquals даст True."),

        Predict("Строка и интерполяция", "Что выведет программа?",
            """
            Console.WriteLine(string.Join(", ", new[] { " пес", "кот ", " ёж" }.Select(x => x.Trim())));
            Console.WriteLine("Порода".Substring(0, 3));
            Console.WriteLine("Порода".ToUpper().Length);
            """,
            "Trim() убирает пробелы, Join склеивает через запятую. Substring(0,3) — 3 символа с начала."),
    };

    // ---------------- ООП ----------------
    static List<GeneratedExample> InheritanceExamples() => new()
    {
        Predict("Наследование", "Что выведет программа?",
            """
            class Animal
            {
                public virtual void Voice() => Console.WriteLine("...");
            }

            class Dog : Animal
            {
                public override void Voice() => Console.WriteLine("Гав");
            }

            Animal a = new Dog();
            a.Voice();
            Animal plain = new Animal();
            plain.Voice();
            """,
            "virtual + override = полиморфизм: в переменной типа Animal лежит Dog, вызывается переопределённый Voice → «Гав»."),

        Predict("Наследование", "Что выведет программа?",
            """
            class Base
            {
                public void Hello() => Console.WriteLine("Base");
            }

            class Child : Base
            {
                public new void Hello() => Console.WriteLine("Child");
            }

            Base b = new Child();
            b.Hello();
            Child c = new Child();
            c.Hello();
            """,
            "new — скрытие, а не переопределение: выбор метода зависит от ТИПА переменной, не от объекта → Base, потом Child."),

        Predict("Наследование", "Что выведет программа?",
            """
            abstract class Shape
            {
                public abstract double Area();
            }

            class Square : Shape
            {
                public double Side { get; set; }
                public override double Area() => Side * Side;
            }

            Console.WriteLine(new Square { Side = 3 }.Area());
            """,
            "abstract-класс нельзя создать, но он обязывает потомка реализовать Area(). 3*3 = 9."),
    };

    static List<GeneratedExample> InterfaceExamples() => new()
    {
        Predict("Интерфейс", "Что выведет программа?",
            """
            interface IWalkable
            {
                void Walk();
            }

            class Dog : IWalkable
            {
                public void Walk() => Console.WriteLine("Пёс идёт");
            }

            class Parrot : IWalkable
            {
                public void Walk() => Console.WriteLine("Попугай идёт");
            }

            foreach (IWalkable w in new IWalkable[] { new Dog(), new Parrot() })
                w.Walk();
            """,
            "Интерфейс — контракт: кто реализовал Walk(), тот попадает в коллекцию IWalkable, независимо от «породы»."),

        Predict("Интерфейс", "Что выведет программа?",
            """
            var arr = new int[] { 3, 1, 2 };
            Array.Sort(arr);
            Console.WriteLine(string.Join(",", arr));
            """,
            "Array.Sort пользуется IComparable, который есть у int: сравнение по значению → 1,2,3."),
    };

    static List<GeneratedExample> DelegateExamples() => new()
    {
        Predict("Делегат", "Что выведет программа?",
            """
            Func<int, int> f = a => a + 2;
            Console.WriteLine(f(10));

            Action<string> log = s => Console.WriteLine($"[log] {s}");
            log("привет");

            Predicate<int> isBig = a => a > 5;
            Console.WriteLine(isBig(7));
            """,
            "Func имеет возвращаемое значение, Action — нет, Predicate возвращает bool. f(10) = 12."),

        Predict("Делегат", "Что выведет программа?",
            """
            int outer = 10;
            Func<int> closure = () => outer;
            outer = 20;
            Console.WriteLine(closure());
            """,
            "Замыкание хранит ПЕРЕМЕННУЮ, а не её значение на момент создания. outer стал 20 → замыкание вернёт 20."),
    };

    static List<GeneratedExample> ExceptionExamples() => new()
    {
        Predict("Исключение", "Что выведет программа?",
            """
            try
            {
                int x = int.Parse("abc");
                Console.WriteLine("не выполнится");
            }
            catch (FormatException e)
            {
                Console.WriteLine("format");
            }
            finally
            {
                Console.WriteLine("finally");
            }
            """,
            "int.Parse(\"abc\") бросает FormatException; catch ловит его, finally выполняется в любом случае."),

        Predict("Исключение", "Что выведет программа?",
            """
            try
            {
                throw new InvalidOperationException();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.GetType().Name);
            }
            """,
            "catch (Exception) ловит производные классы, поэтому InvalidOperationException будет пойман."),
    };

    static List<GeneratedExample> PropertyExamples() => new()
    {
        Predict("Свойство", "Что выведет программа?",
            """
            class Dog
            {
                int age;
                public int Age
                {
                    get => age;
                    set => age = value < 0 ? 0 : value;
                }
            }

            var d = new Dog();
            d.Age = -5;
            Console.WriteLine(d.Age);
            d.Age = 4;
            Console.WriteLine(d.Age);
            """,
            "set фильтрует значение: отрицательное заменяется нулём. Свойство защищает внутренние данные."),
    };

    static List<GeneratedExample> ConstructorExamples() => new()
    {
        Predict("Конструктор", "Что выведет программа?",
            """
            class Dog
            {
                public string Name { get; }
                public Dog(string name) => Name = name;
            }

            var d = new Dog("Барос");
            Console.WriteLine(d.Name);
            // d.Name = "Рекс"; // не скомпилируется: get без set
            """,
            "Свойство только с get (без set) после конструктора неизменяемо — значение задаётся один раз при создании."),

        Predict("Конструктор", "Что выведет программа?",
            """
            class A
            {
                public static string Log = "";
                public A() => Log += "A";
            }

            class B : A
            {
                public B() => Log += "B";
            }

            var b = new B();
            Console.WriteLine(B.Log);
            """,
            "Конструктор базового класса выполняется первым, поэтому лог = AB."),
    };

    static List<GeneratedExample> StructClassExamples() => new()
    {
        Predict("Структура и класс", "Что выведет программа?",
            """
            struct Point { public int X; }
            class RefPoint { public int X; }

            var p = new Point { X = 1 };
            var copy = p;
            copy.X = 9;

            var r = new RefPoint { X = 1 };
            var rCopy = r;
            rCopy.X = 9;

            Console.WriteLine(p.X);
            Console.WriteLine(r.X);
            """,
            "struct копируется по значению → p.X остался 1. class копируется по ссылке → r.X стал 9, rCopy и r — один объект."),
    };

    static List<GeneratedExample> StaticExamples() => new()
    {
        Predict("Статический член", "Что выведет программа?",
            """
            class Kennel
            {
                public static int Total;
                public int Local;
            }

            var a = new Kennel(); a.Local = 1;
            var b = new Kennel(); b.Local = 2;
            Kennel.Total = 5;
            Kennel.Total = 7;

            Console.WriteLine(Kennel.Total);
            Console.WriteLine(a.Local + b.Local);
            """,
            "static Total общий для всех объектов: последнее присваивание победило → 7. Через экземпляр статическое поле трогать нельзя — только Kennel.Total. Local у каждого свой: 1+2=3."),
    };

    static List<GeneratedExample> CollectionExamples() => new()
    {
        Predict("Словарь и коллекции", "Что выведет программа?",
            """
            var stack = new Stack<int>();
            stack.Push(1); stack.Push(2);
            Console.WriteLine(stack.Pop());

            var queue = new Queue<int>();
            queue.Enqueue(1); queue.Enqueue(2);
            Console.WriteLine(queue.Dequeue());

            var d = new Dictionary<string, int> { ["Барос"] = 3 };
            d["Барос"] += 1;
            Console.WriteLine(d["Барос"]);
            """,
            "Stack — LIFO (снимает верх), Queue — FIFO (отдаёт первого). Dictionary ищет по ключу и позволяет править значение."),

        Predict("Словарь и коллекции", "Что выведет программа?",
            """
            var list = new List<int> { 5, 1, 9 };
            list.Sort();
            list.Reverse();
            Console.WriteLine(string.Join(",", list));
            Console.WriteLine(list.Contains(5));
            """,
            "Sort() меняет сам список: 1,5,9 → Reverse() → 9,5,1."),
    };

    static List<GeneratedExample> LinqExamples() => new()
    {
        Predict("LINQ", "Что выведет программа?",
            """
            int[] nums = { 1, 2, 3, 4, 5 };
            var big = nums.Where(n => n > 2).Select(n => n * 10).ToList();
            Console.WriteLine(string.Join(",", big));
            Console.WriteLine(nums.Count(n => n > 2));
            Console.WriteLine(nums.Sum());
            """,
            "Where отбирает 3,4,5, Select умножает: 30,40,50. Count() == 3, Sum() == 15."),

        Predict("LINQ", "Что выведет программа?",
            """
            int calls = 0;
            IEnumerable<int> lazy = Enumerable.Range(1, 3).Where(n => { calls++; return true; });
            Console.WriteLine(calls);
            var taken = lazy.ToList();
            Console.WriteLine(calls);
            """,
            "LINQ ленивый: предикат не вызывается, пока не начали перебор. До ToList() — 0, после — 3."),
    };

    static List<GeneratedExample> ThreadingExamples() => new()
    {
        Predict("Многопоточность", "Что выведет программа?",
            """
            int counter = 0;
            var lockObj = new object();

            void Work()
            {
                for (int i = 0; i < 1000; i++)
                    lock (lockObj) counter++;
            }

            var t1 = new System.Threading.Thread(Work);
            var t2 = new System.Threading.Thread(Work);
            t1.Start(); t2.Start();
            t1.Join(); t2.Join();
            Console.WriteLine(counter == 2000);
            """,
            "lock сериализует доступ к counter, поэтому потерянных инкрементов нет: 2000 и True."),

        Predict("Многопоточность", "Что выведет программа?",
            """
            async Task<int> SlowAsync()
            {
                await Task.Delay(10);
                return 42;
            }

            int result = await SlowAsync();
            Console.WriteLine(result);
            """,
            "await ждёт завершения задачи и достаёт результат из Task<int> → 42."),
    };

    static List<GeneratedExample> MemoryExamples() => new()
    {
        Predict("Память и сборка мусора", "Что выведет программа?",
            """
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cl_demo_" + Guid.NewGuid().ToString("N") + ".txt");
            using (var writer = new System.IO.StreamWriter(path))
            {
                writer.Write("корм");
            }
            Console.WriteLine(System.IO.File.ReadAllText(path));
            Console.WriteLine(System.IO.File.Exists(path));
            System.IO.File.Delete(path);
            """,
            "using сам вызывает Dispose → файл закрывается и его можно прочитать сразу. Без using файл мог остаться незаписанным/заблокированным."),
    };

    static List<GeneratedExample> FileExamples() => new()
    {
        Predict("Файлы и потоки", "Что выведет программа?",
            """
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cl_file_" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(path, "первая\nвторая");
            var lines = File.ReadAllLines(path);
            Console.WriteLine(lines.Length);
            Console.WriteLine(lines[1].ToUpper());
            File.Delete(path);
            """,
            "WriteAllText пишет текст, ReadAllLines режет по переводам строк: 2 строки, вторая — «ВТОРАЯ»."),

        Predict("Файлы и потоки", "Что выведет программа?",
            """
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cl_bin_" + Guid.NewGuid().ToString("N") + ".bin");
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                bw.Write(7);
                bw.Write("привет");
            }
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                Console.WriteLine(br.ReadInt32());
                Console.WriteLine(br.ReadString());
            }
            File.Delete(path);
            """,
            "BinaryWriter пишет числа и строки в байтовом виде; читать надо в том же порядке и тем же BinaryReader."),
    };

    static List<GeneratedExample> GenericExamples(string concept) => new()
    {
        Predict(concept, "Что выведет программа? (пример по теме: " + concept + ")",
            """
            static int Transform(int a)
            {
                a += 2;
                return a * 3;
            }

            Console.WriteLine(Transform(4));
            Console.WriteLine(Transform(-4));
            """,
            "Метод получает копию a: 4 → 6 → 18; -4 → -2 → -6. Внешняя переменная не меняется."),

        new()
        {
            Concept = concept, Format = QuizFormat.MultipleChoice,
            Prompt = $"Коротко: зачем в C# нужен «{concept}»? Выберите самый точный ответ.",
            Code = "",
            Options = new()
            {
                $"Помогает описывать и переиспользовать «{concept}» вместо дублирования кода",
                "Ускоряет программу автоматически",
                "Заменяет собой компилятор",
                "Нужен только для работы с файлами",
            },
            CorrectAnswer = $"Помогает описывать и переиспользовать «{concept}» вместо дублирования кода",
            Explanation = "Общий смысл любой конструкции языка — сократить повторение и сделать код проверяемым.",
        },
    };


    // ---------------- WinForms / WPF примеры ----------------
    /// <summary>
    /// GUI-пример — самодостаточная программа: она создаёт окно, показывает результат
    /// и сама закрывается по таймеру. Поэтому её можно реально запустить кнопкой «Запустить»
    /// (песочница запускает её на desktop-рантайме).
    /// HarnessCode — консольный эквивалент той же логики: по нему эталон ответа
    /// подтверждается запуском без окон (быстро и стабильно на любой машине).
    /// </summary>
    public sealed record GuiRecipe(string Concept, string Decl, string Compute, string Note);

    static readonly GuiRecipe[] GuiRecipes =
    {
        new("Класс",
            "class Dog { public string Name { get; set; } = \"Барос\"; }\nvar a = new Dog();\nvar b = new Dog { Name = \"Рекс\" };",
            "a.Name + \" / \" + b.Name",
            "Два объекта класса независимы: второй получил имя через инициализатор, первый — значение поля по умолчанию."),
        new("Условие if/else",
            "int a = 7;\nstring r = a > 10 ? \"много\" : a > 5 ? \"средне\" : \"мало\";",
            "\"a = \" + a + \" → \" + r",
            "В обработчике клика логика та же, что и в консоли: вместо Console.WriteLine — подпись контрола."),
        new("Цикл",
            "var sb = new System.Text.StringBuilder();\nfor (int i = 0; i < 3; i++) sb.Append(i).Append(' ');",
            "sb.ToString().Trim()",
            "Цикл в обработчике события работает как обычный цикл; Label показывает накопленное."),
        new("Делегат",
            "var sb2 = new System.Text.StringBuilder();\nSystem.Action<string> onClick = s => sb2.Append(s);\nonClick(\"готов\");",
            "\"делегат вызван: \" + (onClick != null ? \"да\" : \"нет\")",
            "Click в GUI — это событие-делегат: += подключает метод. Здесь тот же делегат существует и проверяется на null."),
        new("Свойство",
            "class Counter { int n; public int N { get => n; set => n = value < 0 ? 0 : value; } }\nvar c = new Counter { N = -5 };",
            "c.N + \" (отрицательное защитили нулём)\"",
            "set-доступ фильтрует значение: свойство защищает данные, как и в консоли."),
        new("Массив",
            "int[] m = { 10, 20, 30 };",
            "m[0] + \" + \" + m[^1] + \" = \" + (m[0] + m[^1])",
            "Индексация в GUI та же: m[0] — первый элемент, m[^1] — последний."),
        new("Строка и интерполяция",
            "int a = 10;\nstring s = $\"{a} + 2\";",
            "s + \" (длина \" + s.Length + \")\"",
            "Интерполяция — просто текст с подстановкой: «10 + 2», а не результат 12."),
        new("Исключение",
            "string err = \"\";\ntry { err = int.Parse(\"abc\") + \"\"; } catch (System.FormatException) { err = \"format\"; }",
            "err",
            "Тот же try/catch стоит в обработчике кнопки: падение внутри окна не роняет приложение."),
        new("Словарь и коллекции",
            "var d = new Dictionary<string, int> { [\"Барос\"] = 3 };\nd[\"Барос\"] += 1;",
            "d[\"Барос\"] + \" (корм выдали дважды)\"",
            "Dictionary ищет по ключу: в GUI это может быть счётчик кликов по имени пользователя."),
    };

    static readonly GuiRecipe FallbackRecipe =
        new("Переменная", "int a = 7;\nint b = a * 2;", "a + \" → \" + b",
            "GUI-событие — обычный метод: данные готовятся так же, как в консоли.");

    public static GeneratedExample WinFormsExample(string concept) => GuiExample(concept, "WinForms");

    public static GeneratedExample WpfExample(string concept) => GuiExample(concept, "WPF");

    static GeneratedExample GuiExample(string concept, string flavor)
    {
        var recipe = GuiRecipes.FirstOrDefault(r => r.Concept == concept) ?? FallbackRecipe;

        string title = flavor + ": " + recipe.Concept;
        string code = flavor == "WinForms"
            ? WinFormsSource(recipe.Decl, recipe.Compute, title)
            : WpfSource(recipe.Decl, recipe.Compute, title);

        // Консольный эквивалент: та же логика без окна — по нему проверяется эталон ответа.
        string harness = recipe.Decl + "\nConsole.Write(" + recipe.Compute + ");";

        return new GeneratedExample
        {
            Concept = recipe.Concept,
            Format = QuizFormat.PredictOutput,
            Prompt = flavor + "-пример: что появится в подписи на экране после описанного действия?",
            Code = code.Trim(),
            HarnessCode = harness.Trim(),
            Options = new(),
            Explanation = recipe.Note + " Кнопка «Запустить» покажет это окно вживую (оно закроется само через 1,5 с).",
            NeedsVerification = true,
            IsGui = true,
        };
    }

    /// <summary>
    /// Шаблон собирается посимвольно (не через интерполированную строку): в коде окна много
    /// фигурных скобок, и любой raw-интерполятор начал бы спорить с ними.
    /// </summary>
    static string WinFormsSource(string decl, string compute, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(decl);
        sb.AppendLine();
        sb.AppendLine("var form = new System.Windows.Forms.Form { Text = \"" + title + "\", Width = 460, Height = 190 };");
        sb.AppendLine("var label1 = new System.Windows.Forms.Label { AutoSize = true, Top = 30, Left = 26 };");
        sb.AppendLine("var label2 = new System.Windows.Forms.Label { AutoSize = true, Top = 62, Left = 26, ForeColor = System.Drawing.Color.DimGray };");
        sb.AppendLine("label1.Text = " + compute + ";");
        sb.AppendLine("label2.Text = \"это окно создал код из урока\";");
        sb.AppendLine("form.Controls.Add(label1);");
        sb.AppendLine("form.Controls.Add(label2);");
        sb.AppendLine();
        sb.AppendLine("Console.WriteLine(label1.Text);");
        sb.AppendLine();
        sb.AppendLine("var timer = new System.Windows.Forms.Timer { Interval = 1500 };");
        sb.AppendLine("timer.Tick += (_, _) => { timer.Stop(); form.Close(); };");
        sb.AppendLine("timer.Start();");
        sb.AppendLine("System.Windows.Forms.Application.Run(form);");
        return sb.ToString();
    }

    static string WpfSource(string decl, string compute, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(decl);
        sb.AppendLine();
        sb.AppendLine("var text1 = new System.Windows.Controls.TextBlock");
        sb.AppendLine("{");
        sb.AppendLine("    Text = " + compute + ",");
        sb.AppendLine("    Margin = new System.Windows.Thickness(26, 26, 26, 6),");
        sb.AppendLine("    FontSize = 16,");
        sb.AppendLine("};");
        sb.AppendLine("var text2 = new System.Windows.Controls.TextBlock");
        sb.AppendLine("{");
        sb.AppendLine("    Text = \"это окно создал код из урока\",");
        sb.AppendLine("    Margin = new System.Windows.Thickness(26, 0, 26, 26),");
        sb.AppendLine("    FontSize = 12,");
        sb.AppendLine("    Foreground = System.Windows.Media.Brushes.Gray,");
        sb.AppendLine("};");
        sb.AppendLine("var panel = new System.Windows.Controls.StackPanel();");
        sb.AppendLine("panel.Children.Add(text1);");
        sb.AppendLine("panel.Children.Add(text2);");
        sb.AppendLine();
        sb.AppendLine("var app = new System.Windows.Application();");
        sb.AppendLine("var window = new System.Windows.Window { Title = \"" + title + "\", Width = 460, Height = 190, Content = panel };");
        sb.AppendLine();
        sb.AppendLine("Console.WriteLine(text1.Text);");
        sb.AppendLine("window.Show();");
        sb.AppendLine();
        sb.AppendLine("var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };");
        sb.AppendLine("timer.Tick += (_, _) => { timer.Stop(); window.Close(); };");
        sb.AppendLine("timer.Start();");
        sb.AppendLine("app.Run();");
        return sb.ToString();
    }
}
