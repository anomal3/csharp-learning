using CsharpLearning.Core.Model;

namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Примеры «Что выведет программа?» для курса Rust. Названия концепций совпадают
/// с RustAnalogyLibrary (Analogy.Concept). Каждый пример — полная программа с fn main,
/// детерминированный вывод, без ввода. CorrectAnswer пуст: эталон получает реальный запуск rustc
/// (NeedsVerification = true). Все примеры предварительно скомпилированы и запущены.
/// </summary>
public static class RustExampleGenerator
{
    const string Q = "Что выведет программа?";

    public static IEnumerable<GeneratedExample> For(string concept) => concept switch
    {
        "Переменная и let" => VariableLet(),
        "mut и неизменяемость" => MutEx(),
        "Shadowing" => ShadowEx(),
        "Тип данных" => TypeEx(),
        "Макрос println!" => PrintlnEx(),
        "Функция" => FnEx(),
        "Замыкание" => ClosureEx(),
        "Кортеж" => TupleEx(),
        "Массив" => ArrayEx(),
        "Вектор" => VecEx(),
        "String и &str" => StringEx(),
        "Структура (struct)" => StructEx(),
        "Перечисление (enum)" => EnumEx(),
        "Option" => OptionEx(),
        "Result" => ResultEx(),
        "match" => MatchEx(),
        "Шаблон (pattern)" => PatternEx(),
        "Цикл" => LoopEx(),
        "Диапазон" => RangeEx(),
        "Владение (ownership)" => OwnershipEx(),
        "Move (перемещение)" => MoveEx(),
        "Clone и Copy" => CloneEx(),
        "Ссылка &" => RefEx(),
        "Изменяемая ссылка &mut" => MutRefEx(),
        "Borrow checker" => BorrowEx(),
        "Срез (slice)" => SliceEx(),
        "Время жизни ('a)" => LifetimeEx(),
        "Трейт (trait)" => TraitEx(),
        "Обобщения (generics)" => GenericEx(),
        "Метод и impl" => ImplEx(),
        "Итератор" => IterEx(),
        "HashMap" => HashMapEx(),
        "Модуль и use" => ModuleEx(),
        "Крейт и cargo" => CrateEx(),
        "panic!" => PanicEx(),
        "Оператор ?" => QuestionEx(),
        "Box и умные указатели" => BoxEx(),
        "Arc и Mutex" => ArcEx(),
        "Поток (thread)" => ThreadEx(),
        "Канал (channel)" => ChannelEx(),
        "unsafe" => UnsafeEx(),
        "Тест (#[test])" => TestEx(),
        _ => Array.Empty<GeneratedExample>(),
    };

    static GeneratedExample P(string concept, string prompt, string code, string explanation)
        => new()
        {
            Concept = concept,
            Format = QuizFormat.PredictOutput,
            Prompt = prompt,
            Code = code.Trim(),
            Explanation = explanation,
            NeedsVerification = true,
        };

    static GeneratedExample[] VariableLet() => new[]
    {
        P("Переменная и let", Q,
            """
            fn main() {
                let a = 5;
                let b = a + 2;
                let c = b * a;
                println!("{} {} {}", a, b, c);
            }
            """,
            "Каждая переменная создаётся через let и считается по порядку: a = 5, b = 5 + 2 = 7, c = 7 * 5 = 35. Значения выводятся через пробел."),
    };

    static GeneratedExample[] MutEx() => new[]
    {
        P("mut и неизменяемость", Q,
            """
            fn main() {
                let mut n = 1;
                n += 4;
                n *= 2;
                println!("n = {}", n);
            }
            """,
            "Из-за mut переменную можно менять: 1 + 4 = 5, затем 5 * 2 = 10. Без mut эта программа не скомпилировалась бы."),
        P("mut и неизменяемость", Q,
            """
            fn main() {
                let mut total = 0;
                for i in 1..=4 {
                    total += i;
                    print!("{} ", total);
                }
                println!();
                println!("итого {}", total);
            }
            """,
            "Накопительная сумма: 1, 3 (1+2), 6, 10. print! не переводит строку, поэтому числа идут в одну строку, а println!() в конце просто заканчивает её."),
    };

    static GeneratedExample[] ShadowEx() => new[]
    {
        P("Shadowing", "Что выведет программа? Обрати внимание на фигурные скобки.",
            """
            fn main() {
                let x = 5;
                let x = x * 2;
                {
                    let x = x + 100;
                    println!("внутри: {}", x);
                }
                println!("снаружи: {}", x);
            }
            """,
            "Каждый let создаёт НОВУЮ переменную x. Внутри блока x = 10 + 100 = 110, но после } затенение заканчивается, и снаружи снова виден x = 10."),
        P("Shadowing", Q,
            """
            fn main() {
                let spaces = "   ";
                let spaces = spaces.len();
                println!("{}", spaces);
            }
            """,
            "Shadowing позволяет сменить даже ТИП: сначала spaces — строка из трёх пробелов, затем новая переменная spaces — число (длина строки) 3. С mut так сделать нельзя."),
    };

    static GeneratedExample[] TypeEx() => new[]
    {
        P("Тип данных", Q,
            """
            fn main() {
                let a: i32 = 7;
                let b: f64 = 2.0;
                let c = a as f64 / b;
                let d = a / 2;
                println!("{} {}", c, d);
            }
            """,
            "c — деление дробных чисел: 7.0 / 2.0 = 3.5. d — деление целых, дробная часть отбрасывается: 7 / 2 = 3."),
        P("Тип данных", Q,
            """
            fn main() {
                let big: u8 = 250;
                let sum = big.wrapping_add(10);
                let letter = 'R';
                let code = letter as u32;
                println!("{} {}", sum, code);
            }
            """,
            "u8 вмещает 0..255. wrapping_add «заворачивает» результат: 250 + 10 = 260, 260 - 256 = 4. Символ 'R' в Unicode имеет код 82."),
    };

    static GeneratedExample[] PrintlnEx() => new[]
    {
        P("Макрос println!", Q,
            """
            fn main() {
                let name = "Рекс";
                let age = 3;
                println!("{} - {}", name, age);
                println!("{1} {0}", "a", "b");
                print!("x");
                print!("y\n");
                println!("{name} {age}");
            }
            """,
            "Пары {} берут аргументы по порядку. {1} {0} выбирают по номеру, поэтому порядок меняется на обратный. print! не добавляет перевод строки, но \\n в конце второго print! его ставит. {name} подставляет переменную с таким именем."),
        P("Макрос println!", Q,
            """
            fn main() {
                println!("[{:>5}]", 42);
                println!("[{:<5}]", 42);
                println!("[{:05}]", 42);
                println!("[{:.2}]", 3.14159);
            }
            """,
            "{:>5} — выровнять вправо в ширине 5, {:<5} — влево, {:05} — дополнить нулями слева, {:.2} — два знака после запятой (3.14159 округляется до 3.14)."),
    };

    static GeneratedExample[] FnEx() => new[]
    {
        P("Функция", Q,
            """
            fn square(n: i32) -> i32 {
                n * n
            }

            fn add_one(n: i32) -> i32 {
                n + 1
            }

            fn main() {
                println!("{}", add_one(square(3)));
                println!("{}", square(add_one(3)));
            }
            """,
            "Вложенные вызовы выполняются изнутри наружу. add_one(square(3)) = add_one(9) = 10. square(add_one(3)) = square(4) = 16. Последнее выражение без ; — результат функции."),
    };

    static GeneratedExample[] ClosureEx() => new[]
    {
        P("Замыкание", Q,
            """
            fn main() {
                let k = 3;
                let mul = |x| x * k;
                let mut count = 0;
                let mut inc = || {
                    count += 1;
                    count
                };
                inc();
                inc();
                println!("{} {}", mul(5), inc());
            }
            """,
            "mul запомнило k = 3, поэтому mul(5) = 15. inc каждый раз увеличивает захваченный count: после двух первых вызовов count = 2, третий вызов в println! возвращает 3."),
        P("Замыкание", "Что выведет программа? Замыкание создано с move.",
            """
            fn main() {
                let mut n = 1;
                let add = move |x| x + n;
                n = 100;
                println!("{} {}", add(1), n);
            }
            """,
            "move копирует захваченное в замыкание. Число — Copy, поэтому замыкание хранит СВОЮ копию n = 1, и add(1) = 2. Исходная n изменилась на 100, но замыкание этого не видит."),
    };

    static GeneratedExample[] TupleEx() => new[]
    {
        P("Кортеж", Q,
            """
            fn main() {
                let t = ("Хлеб", 2, 35.5);
                let (name, qty, price) = t;
                println!("{} x{} = {}", name, qty, qty as f64 * price);
                println!("{}", t.1 + 10);
            }
            """,
            "Деструктуризация разбирает кортеж на три переменные. 2 * 35.5 = 71 (дробное число 71.0 печатается как 71). t.1 — второй элемент (счёт с нуля) = 2, плюс 10 = 12."),
    };

    static GeneratedExample[] ArrayEx() => new[]
    {
        P("Массив", Q,
            """
            fn main() {
                let mut a = [1, 2, 3, 4, 5];
                a[0] = 10;
                let sum: i32 = a.iter().sum();
                println!("{} {}", a.len(), sum);
            }
            """,
            "Длина массива не меняется — 5. Но первый элемент заменён на 10, поэтому сумма 10 + 2 + 3 + 4 + 5 = 24."),
        P("Массив", Q,
            """
            fn main() {
                let z = [7; 3];
                let grid = [[0; 2]; 2];
                println!("{:?}", z);
                println!("{:?}", grid);
            }
            """,
            "[7; 3] — три элемента, каждый равен 7. [[0; 2]; 2] — два массива по два нуля. Формат {:?} печатает массивы в квадратных скобках через запятую."),
    };

    static GeneratedExample[] VecEx() => new[]
    {
        P("Вектор", Q,
            """
            fn main() {
                let mut v = vec![1, 2, 3];
                v.push(4);
                let last = v.pop();
                v.insert(0, 9);
                println!("{:?} {:?} {}", v, last, v.len());
            }
            """,
            "После push вектор [1, 2, 3, 4]. pop забирает последний элемент и возвращает Some(4), остаётся [1, 2, 3]. insert(0, 9) вставляет 9 в начало: [9, 1, 2, 3], длина 4."),
        P("Вектор", Q,
            """
            fn main() {
                let v = vec![10, 20, 30];
                println!("{:?}", v.get(1));
                println!("{:?}", v.get(7));
            }
            """,
            "get возвращает Option: Some(20) для существующего индекса и None для выходящего за границы. v[7] в этом случае вызвал бы panic."),
    };

    static GeneratedExample[] StringEx() => new[]
    {
        P("String и &str", Q,
            """
            fn main() {
                let mut s = String::from("Привет");
                s.push_str(", мир");
                s.push('!');
                let t = &s[..];
                println!("{} {} {}", t, s.len(), s.chars().count());
            }
            """,
            "Строка: «Привет, мир!». len() считает БАЙТЫ: кириллическая буква в UTF-8 занимает 2 байта, всего 12 + 8 + 1 = 21. chars().count() считает символы: 6 + 5 + 1 = 12."),
    };

    static GeneratedExample[] StructEx() => new[]
    {
        P("Структура (struct)", Q,
            """
            struct Dog {
                name: String,
                age: u32,
            }

            fn main() {
                let mut d = Dog { name: String::from("Рекс"), age: 3 };
                d.age += 1;
                let Dog { name, age } = d;
                println!("{} {}", name, age);
            }
            """,
            "Переменная d объявлена с mut, поэтому поле age можно изменить: 3 + 1 = 4. Затем структура разбирается на поля name и age."),
    };

    static GeneratedExample[] EnumEx() => new[]
    {
        P("Перечисление (enum)", Q,
            """
            enum Shape {
                Circle(f64),
                Rect(f64, f64),
            }

            fn area(s: &Shape) -> f64 {
                match s {
                    Shape::Circle(r) => 3.0 * r * r,
                    Shape::Rect(w, h) => w * h,
                }
            }

            fn main() {
                println!("{}", area(&Shape::Circle(2.0)));
                println!("{}", area(&Shape::Rect(2.0, 3.5)));
            }
            """,
            "Каждый вариант enum хранит свои данные. Круг: 3.0 * 2.0 * 2.0 = 12. Прямоугольник: 2.0 * 3.5 = 7. Дробные числа без дробной части печатаются без «.0»."),
    };

    static GeneratedExample[] OptionEx() => new[]
    {
        P("Option", Q,
            """
            fn main() {
                let a: Option<i32> = Some(5);
                let b: Option<i32> = None;
                println!("{} {}", a.unwrap_or(0), b.unwrap_or(-1));
                println!("{:?} {:?}", a.map(|x| x * 2), b.map(|x| x * 2));
            }
            """,
            "unwrap_or достаёт значение, а для None подставляет запасное: 5 и -1. map применяется только к Some: Some(10), а None остаётся None."),
        P("Option", Q,
            """
            fn main() {
                let v = vec![3, 8, 5];
                match v.iter().position(|&x| x == 8) {
                    Some(i) => println!("индекс {}", i),
                    None => println!("нет"),
                }
                println!("{:?}", v.iter().position(|&x| x == 1));
            }
            """,
            "position ищет первый подходящий элемент и возвращает Option<usize>. 8 стоит на позиции 1 (счёт с нуля), поэтому Some(1). Единицы нет, результат None."),
    };

    static GeneratedExample[] ResultEx() => new[]
    {
        P("Result", Q,
            """
            fn main() {
                let a = "42".parse::<i32>();
                let b = "abc".parse::<i32>();
                println!("{:?}", a);
                println!("{}", b.is_err());
                match b {
                    Ok(n) => println!("число {}", n),
                    Err(_) => println!("не число"),
                }
                println!("{}", "x".parse::<i32>().unwrap_or(0));
            }
            """,
            "parse возвращает Result: из «42» получится Ok(42), из «abc» — Err. is_err() = true. match выбирает ветку Err, а unwrap_or(0) при ошибке подставляет 0."),
    };

    static GeneratedExample[] MatchEx() => new[]
    {
        P("match", Q,
            """
            fn describe(n: i32) -> &'static str {
                match n {
                    0 => "ноль",
                    1 | 2 => "мало",
                    3..=9 => "несколько",
                    _ => "много",
                }
            }

            fn main() {
                for n in [0, 2, 5, 10] {
                    println!("{}", describe(n));
                }
            }
            """,
            "match проверяет ветки сверху вниз. 0 — «ноль», 2 подходит под 1 | 2 — «мало», 5 попадает в диапазон 3..=9 — «несколько», 10 уходит в _ — «много»."),
        P("match", Q,
            """
            fn main() {
                let n = 20;
                let s = match n {
                    x if x > 0 => "положительное",
                    x if x > 10 => "большое",
                    _ => "другое",
                };
                println!("{}", s);
            }
            """,
            "Выигрывает ПЕРВАЯ подошедшая ветка. 20 > 0, значит срабатывает «положительное», а до проверки x > 10 дело не доходит."),
    };

    static GeneratedExample[] PatternEx() => new[]
    {
        P("Шаблон (pattern)", Q,
            """
            fn main() {
                let pair = (3, -3);
                match pair {
                    (x, y) if x + y == 0 => println!("противоположные"),
                    (x, _) if x % 2 == 1 => println!("нечётное"),
                    _ => println!("другое"),
                }
                let maybe = Some(7);
                if let Some(n) = maybe {
                    println!("{}", n + 1);
                }
            }
            """,
            "Первая ветка подходит: 3 + (-3) = 0, поэтому печатается «противоположные» (остальные ветки не проверяются). if let достаёт 7 из Some и печатает 7 + 1 = 8."),
        P("Шаблон (pattern)", Q,
            """
            fn main() {
                let mut stack = vec![1, 2, 3];
                while let Some(top) = stack.pop() {
                    print!("{} ", top);
                }
                println!("конец");
            }
            """,
            "while let повторяется, пока pop возвращает Some. pop забирает с конца: 3, 2, 1. Когда вектор пуст, pop возвращает None, и цикл заканчивается."),
    };

    static GeneratedExample[] LoopEx() => new[]
    {
        P("Цикл", Q,
            """
            fn main() {
                let mut i = 0;
                let r = loop {
                    i += 3;
                    if i > 10 {
                        break i * 2;
                    }
                };
                println!("{}", r);
            }
            """,
            "i растёт: 3, 6, 9, 12. На 12 условие i > 10 выполнено, и loop возвращает через break значение 12 * 2 = 24."),
        P("Цикл", Q,
            """
            fn main() {
                for n in 1..=6 {
                    if n % 2 == 0 {
                        continue;
                    }
                    print!("{} ", n);
                }
                println!();
            }
            """,
            "continue пропускает остаток шага для чётных чисел. Печатаются только нечётные: 1 3 5."),
    };

    static GeneratedExample[] RangeEx() => new[]
    {
        P("Диапазон", Q,
            """
            fn main() {
                let a: Vec<i32> = (1..4).collect();
                let b: Vec<i32> = (1..=4).collect();
                let c: Vec<i32> = (1..10).step_by(4).collect();
                let d: Vec<i32> = (0..3).rev().collect();
                println!("{:?} {:?}", a, b);
                println!("{:?} {:?}", c, d);
            }
            """,
            "1..4 не включает 4, а 1..=4 включает. step_by(4) идёт с шагом 4: 1, 5, 9. rev() переворачивает 0..3, получается 2, 1, 0."),
        P("Диапазон", Q,
            """
            fn main() {
                let n = (5..1).count();
                let m = (1..1).count();
                println!("{} {}", n, m);
            }
            """,
            "Диапазон не умеет считать назад: 5..1 пуст. 1..1 тоже пуст, так как правая граница не входит. Оба счётчика равны 0."),
    };

    static GeneratedExample[] OwnershipEx() => new[]
    {
        P("Владение (ownership)", Q,
            """
            struct Noisy(&'static str);

            impl Drop for Noisy {
                fn drop(&mut self) {
                    println!("drop {}", self.0);
                }
            }

            fn main() {
                let _a = Noisy("a");
                {
                    let _b = Noisy("b");
                    println!("внутри");
                }
                let _c = Noisy("c");
                println!("конец");
            }
            """,
            "Значение освобождается, когда его владелец выходит из области видимости. b живёт только внутри { }, поэтому drop b идёт сразу после «внутри». В конце main переменные освобождаются в обратном порядке создания: сначала c, потом a."),
    };

    static GeneratedExample[] MoveEx() => new[]
    {
        P("Move (перемещение)", Q,
            """
            fn add_item(mut v: Vec<i32>) -> Vec<i32> {
                v.push(4);
                v
            }

            fn main() {
                let v = vec![1, 2, 3];
                let v = add_item(v);
                println!("{:?}", v);
            }
            """,
            "Вектор ПЕРЕЕЗЖАЕТ в функцию (move), та добавляет 4 и возвращает его владельцу обратно. Новая переменная v (shadowing) снова им владеет: [1, 2, 3, 4]."),
        P("Move (перемещение)", Q,
            """
            fn main() {
                let names = vec![String::from("a"), String::from("b")];
                for n in names {
                    print!("{} ", n.to_uppercase());
                }
                println!();
            }
            """,
            "for n in names забирает вектор во владение (move): после цикла names использовать нельзя. Строки приводятся к верхнему регистру: A B."),
    };

    static GeneratedExample[] CloneEx() => new[]
    {
        P("Clone и Copy", Q,
            """
            fn main() {
                let a = String::from("кот");
                let mut b = a.clone();
                b.push('!');
                println!("{} {}", a, b);
                let x = 5;
                let mut y = x;
                y += 1;
                println!("{} {}", x, y);
            }
            """,
            "clone делает независимую копию строки: изменение b не затрагивает a. Число — тип Copy, при let y = x копируется само, поэтому x остаётся 5, а y становится 6."),
    };

    static GeneratedExample[] RefEx() => new[]
    {
        P("Ссылка &", Q,
            """
            fn total_len(a: &String, b: &String) -> usize {
                a.len() + b.len()
            }

            fn main() {
                let a = String::from("abc");
                let b = String::from("de");
                println!("{}", total_len(&a, &b));
                println!("{} {}", a, b);
                let r = &a;
                let r2 = &a;
                println!("{} {}", r, r2);
            }
            """,
            "&a — ссылка: функция только заглядывает в строки (3 + 2 = 5), а владельцами остаются a и b, поэтому их можно печатать после вызова. Несколько ссылок для чтения одновременно разрешены."),
    };

    static GeneratedExample[] MutRefEx() => new[]
    {
        P("Изменяемая ссылка &mut", Q,
            """
            fn grow(v: &mut Vec<i32>) {
                v.push(v.len() as i32);
            }

            fn main() {
                let mut v = vec![10];
                grow(&mut v);
                grow(&mut v);
                println!("{:?}", v);
            }
            """,
            "grow добавляет в конец текущую длину вектора. Первый вызов: длина 1, получилось [10, 1]. Второй: длина 2, получилось [10, 1, 2]."),
        P("Изменяемая ссылка &mut", Q,
            """
            fn main() {
                let mut x = 5;
                {
                    let r = &mut x;
                    *r += 10;
                }
                println!("{}", x);
            }
            """,
            "Через &mut значение можно менять; *r — «то, на что указывает r». Ссылка живёт только внутри блока, после него x снова доступна, и в ней уже 15."),
    };

    static GeneratedExample[] BorrowEx() => new[]
    {
        P("Borrow checker", Q,
            """
            fn main() {
                let mut s = String::from("a");
                let r = &s;
                println!("{}", r);
                let m = &mut s;
                m.push('b');
                println!("{}", m);
            }
            """,
            "Программа компилируется: последнее использование r — на строке с первым println!, после него ссылка «умирает». Поэтому изменяемую ссылку m брать можно. Выводится «a», затем «ab»."),
    };

    static GeneratedExample[] SliceEx() => new[]
    {
        P("Срез (slice)", Q,
            """
            fn main() {
                let a = [10, 20, 30, 40, 50];
                let s = &a[1..4];
                println!("{:?} {}", s, s.len());
                let text = "hello world";
                println!("{}", &text[..5]);
                println!("{:?}", &a[3..]);
            }
            """,
            "Срез 1..4 берёт элементы с индексами 1, 2, 3 (правая граница не входит): [20, 30, 40], длина 3. &text[..5] — первые 5 символов «hello». &a[3..] — с индекса 3 и до конца: [40, 50]."),
    };

    static GeneratedExample[] LifetimeEx() => new[]
    {
        P("Время жизни ('a)", Q,
            """
            fn longest<'a>(a: &'a str, b: &'a str) -> &'a str {
                if a.len() >= b.len() { a } else { b }
            }

            fn main() {
                let x = String::from("abcd");
                let res;
                {
                    let y = String::from("xyz");
                    res = longest(&x, &y).to_string();
                }
                println!("{}", res);
                println!("{}", longest("ab", "cd"));
            }
            """,
            "'abcd' длиннее 'xyz', поэтому longest возвращает первую строку (копия уходит в res и переживает y). Для равных длин условие >= выбирает первый аргумент: «ab»."),
    };

    static GeneratedExample[] TraitEx() => new[]
    {
        P("Трейт (trait)", Q,
            """
            trait Speak {
                fn speak(&self) -> String;
                fn twice(&self) -> String {
                    format!("{} {}", self.speak(), self.speak())
                }
            }

            struct Dog;
            struct Cat;

            impl Speak for Dog {
                fn speak(&self) -> String { String::from("Гав") }
            }

            impl Speak for Cat {
                fn speak(&self) -> String { String::from("Мяу") }
                fn twice(&self) -> String { String::from("Мяу-мяу!") }
            }

            fn main() {
                let zoo: Vec<Box<dyn Speak>> = vec![Box::new(Dog), Box::new(Cat)];
                for a in zoo.iter() {
                    println!("{}", a.twice());
                }
            }
            """,
            "У Dog метод twice не переопределён — используется реализация трейта по умолчанию: «Гав Гав». Cat подменяет twice своим вариантом: «Мяу-мяу!»."),
    };

    static GeneratedExample[] GenericEx() => new[]
    {
        P("Обобщения (generics)", Q,
            """
            fn largest<T: PartialOrd + Copy>(v: &[T]) -> T {
                let mut m = v[0];
                for &x in v {
                    if x > m {
                        m = x;
                    }
                }
                m
            }

            fn main() {
                println!("{}", largest(&[3, 9, 2]));
                println!("{}", largest(&[1.5, 0.5]));
                println!("{}", largest(&['a', 'z', 'm']));
            }
            """,
            "Одна функция работает с целыми, дробными числами и символами. Максимумы: 9, 1.5 и 'z' (символы сравниваются по коду)."),
    };

    static GeneratedExample[] ImplEx() => new[]
    {
        P("Метод и impl", Q,
            """
            struct Counter {
                n: u32,
            }

            impl Counter {
                fn new() -> Counter {
                    Counter { n: 0 }
                }
                fn inc(&mut self) {
                    self.n += 1;
                }
                fn get(&self) -> u32 {
                    self.n
                }
            }

            fn main() {
                let mut c = Counter::new();
                c.inc();
                c.inc();
                c.inc();
                println!("{}", c.get());
            }
            """,
            "Counter::new() — ассоциированная функция (без self), создаёт счётчик с нулём. Три вызова inc прибавляют по единице, get возвращает 3."),
        P("Метод и impl", Q,
            """
            struct Rect {
                w: u32,
                h: u32,
            }

            impl Rect {
                fn square(s: u32) -> Rect {
                    Rect { w: s, h: s }
                }
                fn area(&self) -> u32 {
                    self.w * self.h
                }
                fn can_hold(&self, other: &Rect) -> bool {
                    self.w >= other.w && self.h >= other.h
                }
            }

            fn main() {
                let big = Rect::square(4);
                let small = Rect { w: 2, h: 3 };
                println!("{} {}", big.area(), small.area());
                println!("{} {}", big.can_hold(&small), small.can_hold(&big));
            }
            """,
            "Площади: 4 * 4 = 16 и 2 * 3 = 6. Большой прямоугольник вмещает маленький (4 >= 2 и 4 >= 3) — true, а наоборот (2 >= 4 неверно) — false."),
    };

    static GeneratedExample[] IterEx() => new[]
    {
        P("Итератор", Q,
            """
            fn main() {
                let v = vec![1, 2, 3, 4, 5, 6];
                let r: Vec<i32> = v.iter().filter(|&&x| x % 2 == 0).map(|x| x * 10).collect();
                println!("{:?}", r);
                let s: i32 = v.iter().sum();
                println!("{} {}", s, v.iter().any(|&x| x > 5));
            }
            """,
            "filter оставляет чётные 2, 4, 6, map умножает их на 10, collect собирает вектор: [20, 40, 60]. Сумма всех элементов 21; число 6 больше 5, поэтому any даёт true."),
        P("Итератор", "Что выведет программа? Обрати внимание на порядок строк.",
            """
            fn main() {
                let it = (1..4).map(|x| {
                    println!("map {}", x);
                    x * 2
                });
                println!("до");
                let v: Vec<i32> = it.collect();
                println!("{:?}", v);
            }
            """,
            "Итераторы ленивые: пока не вызван collect, map ничего не делает. Поэтому «до» печатается первым, затем map 1, map 2, map 3 и в конце [2, 4, 6]."),
    };

    static GeneratedExample[] HashMapEx() => new[]
    {
        P("HashMap", Q,
            """
            use std::collections::HashMap;

            fn main() {
                let mut m = HashMap::new();
                m.insert("a", 1);
                m.insert("a", 5);
                *m.entry("b").or_insert(0) += 2;
                *m.entry("b").or_insert(0) += 2;
                println!("{:?} {:?} {:?} {}", m.get("a"), m.get("b"), m.get("c"), m.len());
            }
            """,
            "Повторный insert с тем же ключом заменяет значение: a = 5. entry(\"b\").or_insert(0) создаёт 0 при первом обращении и затем два раза прибавляет 2, итого 4. Ключа c нет, get даёт None. Всего два ключа."),
    };

    static GeneratedExample[] ModuleEx() => new[]
    {
        P("Модуль и use", Q,
            """
            mod zoo {
                pub fn feed() -> &'static str {
                    "ам"
                }
                pub mod cage {
                    pub fn lock() -> String {
                        format!("{}-щёлк", super::feed())
                    }
                }
            }

            use zoo::cage::lock;

            fn main() {
                println!("{}", zoo::feed());
                println!("{}", lock());
            }
            """,
            "pub делает функции видимыми снаружи модуля. use подтягивает lock, поэтому её можно звать без полного пути. super::feed() обращается к функции родительского модуля: «ам-щёлк»."),
    };

    static GeneratedExample[] CrateEx() => new[]
    {
        P("Крейт и cargo", Q,
            """
            use std::cmp::{max, min};

            fn main() {
                println!("{} {}", max(3, 8), min(3, 8));
                println!("{}", i32::MAX.wrapping_add(1));
            }
            """,
            "Стандартная библиотека std подключается без cargo. max и min дают 8 и 3. wrapping_add при переполнении «заворачивает» максимальное i32 (2147483647) в минимальное: -2147483648."),
    };

    static GeneratedExample[] PanicEx() => new[]
    {
        P("panic!", Q,
            """
            fn main() {
                let v = vec![1, 2, 3];
                println!("{:?}", v.get(5));
                println!("{:?}", 10i32.checked_div(0));
                println!("{}", v[1]);
            }
            """,
            "get и checked_div вместо падения возвращают None: индекса 5 нет, делить на ноль нельзя. v[1] — обычный доступ, значение 2. Так проверяют ошибки без panic."),
        P("panic!", "Что программа выведет в стандартный вывод (stdout)? Сообщение о panic идёт в поток ошибок.",
            """
            fn main() {
                let v = vec![1, 2, 3];
                println!("до");
                let i = v.len() + 2;
                println!("{}", v[i]);
                println!("после");
            }
            """,
            "Индекс 5 выходит за границы вектора — происходит panic, программа останавливается. В stdout попадает только «до»; сообщение об ошибке уходит в stderr, а «после» не печатается."),
    };

    static GeneratedExample[] QuestionEx() => new[]
    {
        P("Оператор ?", Q,
            """
            fn sum(a: &str, b: &str) -> Result<i32, std::num::ParseIntError> {
                let x = a.parse::<i32>()?;
                let y = b.parse::<i32>()?;
                Ok(x + y)
            }

            fn main() {
                println!("{:?}", sum("2", "3"));
                println!("{}", sum("2", "x").is_err());
                match sum("7", "8") {
                    Ok(n) => println!("{}", n),
                    Err(_) => println!("ошибка"),
                }
            }
            """,
            "sum(\"2\", \"3\") даёт Ok(5). Во втором вызове y не разбирается, оператор ? сразу возвращает Err из функции, поэтому is_err() = true. Третий вызов: 7 + 8 = 15."),
        P("Оператор ?", Q,
            """
            fn first_even_double(v: &[i32]) -> Option<i32> {
                let x = v.iter().find(|&&n| n % 2 == 0)?;
                Some(x * 2)
            }

            fn main() {
                println!("{:?}", first_even_double(&[3, 4, 6]));
                println!("{:?}", first_even_double(&[1, 3]));
            }
            """,
            "? работает и с Option. В первом списке первое чётное — 4, удвоенное даёт Some(8). Во втором чётных нет, find возвращает None, и ? тут же возвращает None из функции."),
    };

    static GeneratedExample[] BoxEx() => new[]
    {
        P("Box и умные указатели", Q,
            """
            enum List {
                Cons(i32, Box<List>),
                Nil,
            }
            use List::{Cons, Nil};

            fn sum(l: &List) -> i32 {
                match l {
                    Cons(v, rest) => v + sum(rest),
                    Nil => 0,
                }
            }

            fn main() {
                let l = Cons(1, Box::new(Cons(2, Box::new(Cons(3, Box::new(Nil))))));
                println!("{}", sum(&l));
                let b = Box::new(41);
                println!("{}", *b + 1);
            }
            """,
            "Box позволяет типу ссылаться на самого себя (иначе размер был бы бесконечным). sum рекурсивно складывает 1 + 2 + 3 + 0 = 6. *b достаёт число из коробки: 41 + 1 = 42."),
    };

    static GeneratedExample[] ArcEx() => new[]
    {
        P("Arc и Mutex", Q,
            """
            use std::sync::{Arc, Mutex};
            use std::thread;

            fn main() {
                let c = Arc::new(Mutex::new(0));
                let mut hs = vec![];
                for _ in 0..4 {
                    let c = Arc::clone(&c);
                    hs.push(thread::spawn(move || {
                        for _ in 0..100 {
                            *c.lock().unwrap() += 1;
                        }
                    }));
                }
                for h in hs {
                    h.join().unwrap();
                }
                println!("{}", *c.lock().unwrap());
            }
            """,
            "Четыре потока по 100 раз увеличивают общий счётчик. Mutex гарантирует, что прибавления не теряются, а join дожидается всех потоков: 4 * 100 = 400. Порядок потоков не важен, итог один."),
    };

    static GeneratedExample[] ThreadEx() => new[]
    {
        P("Поток (thread)", Q,
            """
            use std::thread;

            fn main() {
                let h = thread::spawn(|| (1..=10).sum::<i32>());
                println!("до");
                let r = h.join().unwrap();
                println!("сумма {}", r);
            }
            """,
            "Поток считает сумму 1..=10 = 55 и возвращает её. Он ничего не печатает, поэтому порядок строк определён: «до», затем join ждёт результат и печатается «сумма 55»."),
        P("Поток (thread)", Q,
            """
            use std::thread;

            fn main() {
                let v = vec![1, 2, 3];
                let h = thread::spawn(move || v.len());
                println!("{}", h.join().unwrap());
            }
            """,
            "move передаёт вектор во владение потоку (иначе компилятор не гарантировал бы, что вектор доживёт). Поток возвращает длину 3, join достаёт её."),
    };

    static GeneratedExample[] ChannelEx() => new[]
    {
        P("Канал (channel)", Q,
            """
            use std::sync::mpsc;
            use std::thread;

            fn main() {
                let (tx, rx) = mpsc::channel();
                let h = thread::spawn(move || {
                    for i in 1..=3 {
                        tx.send(i * i).unwrap();
                    }
                });
                for got in rx {
                    println!("получено {}", got);
                }
                h.join().unwrap();
            }
            """,
            "Один отправитель шлёт сообщения по порядку: 1, 4, 9 — порядок в канале сохраняется. Цикл for по rx заканчивается, когда поток завершился и tx уничтожен."),
    };

    static GeneratedExample[] UnsafeEx() => new[]
    {
        P("unsafe", Q,
            """
            fn main() {
                let mut x = 10;
                let p = &mut x as *mut i32;
                unsafe {
                    *p += 5;
                    println!("{}", *p);
                }
                println!("{}", x);
            }
            """,
            "Разыменовать сырой указатель можно только в unsafe. Через него значение x меняется на 15, и оно же видно через саму переменную x."),
    };

    static GeneratedExample[] TestEx() => new[]
    {
        P("Тест (#[test])", Q,
            """
            fn add(a: i32, b: i32) -> i32 {
                a + b
            }

            #[cfg(test)]
            mod tests {
                use super::*;

                #[test]
                fn works() {
                    assert_eq!(add(2, 2), 4);
                }
            }

            fn main() {
                println!("{}", add(2, 3));
                assert_eq!(add(1, 1), 2);
                assert!(add(1, 1) > 1, "мало");
                println!("ok");
            }
            """,
            "Модуль tests с атрибутом #[cfg(test)] попадает в сборку только при cargo test, обычный запуск его не видит. Проверки assert в main проходят молча: add(1, 1) = 2 и 2 > 1. Печатается 5 и ok."),
    };
}
