namespace CsharpLearning.Core.Forms;

/// <summary>
/// Оконные примеры для уроков: только Label, Button, TextBox, CheckBox, ListBox, PictureBox и ProgressBar.
/// Каждый пример — маленькое окно, которое работает прямо в приложении, и настоящий код WinForms рядом.
/// </summary>
public static class FormExamples
{
    // ---------- короткие конструкторы описаний ----------
    static ControlSpec Label(string name, string text, int x, int y, bool visible = true) => new() { Name = name, Kind = ControlKind.Label, Text = text, X = x, Y = y, Visible = visible };
    static ControlSpec Button(string name, string text, int x, int y, int w, params HandlerSpec[] h) => new() { Name = name, Kind = ControlKind.Button, Text = text, X = x, Y = y, W = w, H = 32, Handlers = h.ToList() };
    static ControlSpec Box(string name, int x, int y, int w, string text = "", params HandlerSpec[] h) => new() { Name = name, Kind = ControlKind.TextBox, Text = text, X = x, Y = y, W = w, H = 24, Handlers = h.ToList() };
    static ControlSpec Check(string name, string text, int x, int y, params HandlerSpec[] h) => new() { Name = name, Kind = ControlKind.CheckBox, Text = text, X = x, Y = y, Handlers = h.ToList() };
    static ControlSpec List(string name, int x, int y, int w, int h) => new() { Name = name, Kind = ControlKind.ListBox, X = x, Y = y, W = w, H = h };
    static ControlSpec Picture(string name, string pic, int x, int y, int w, int h) => new() { Name = name, Kind = ControlKind.PictureBox, Picture = pic, X = x, Y = y, W = w, H = h };
    static ControlSpec Progress(string name, int x, int y, int w, int value) => new() { Name = name, Kind = ControlKind.ProgressBar, X = x, Y = y, W = w, H = 22, Value = value };

    static HandlerSpec Click(string name, params ActionSpec[] a) => new() { Event = "Click", Name = name, Actions = a.ToList() };
    static HandlerSpec Changed(string name, params ActionSpec[] a) => new() { Event = "CheckedChanged", Name = name, Actions = a.ToList() };

    static ActionSpec SetText(string target, string value) => new() { Kind = ActionKind.SetText, Target = target, Value = value };
    static ActionSpec SetVar(string target, string value) => new() { Kind = ActionKind.SetVar, Target = target, Value = value };
    static ActionSpec Inc(string target, string by = "") => new() { Kind = ActionKind.Inc, Target = target, Value = by };
    static ActionSpec Add(string target, string value) => new() { Kind = ActionKind.AddItem, Target = target, Value = value };
    static ActionSpec Clear(string target) => new() { Kind = ActionKind.ClearItems, Target = target };
    static ActionSpec Visible(string target, string value) => new() { Kind = ActionKind.SetVisible, Target = target, Value = value };
    static ActionSpec Pic(string target, string pic) => new() { Kind = ActionKind.SetPicture, Target = target, Value = pic };
    static ActionSpec Prog(string target, string value) => new() { Kind = ActionKind.SetProgress, Target = target, Value = value };
    static ActionSpec Msg(string value) => new() { Kind = ActionKind.Message, Value = value };
    static ActionSpec If(string cond, ActionSpec[] then, ActionSpec[]? otherwise = null) => new() { Kind = ActionKind.If, Condition = cond, Then = then.ToList(), Else = (otherwise ?? Array.Empty<ActionSpec>()).ToList() };
    static ActionSpec For(string v, string from, string to, params ActionSpec[] body) => new() { Kind = ActionKind.For, Target = v, From = from, To = to, Body = body.ToList() };
    static ActionSpec Try(ActionSpec[] body, ActionSpec[] onError) => new() { Kind = ActionKind.Try, Body = body.ToList(), Catch = onError.ToList() };

    public static readonly IReadOnlyList<FormExample> All = new List<FormExample>
    {
        new()
        {
            Id = "hello", Title = "Привет, окно", Tags = new[] { "старт и тулчейн", "программа и Main", "классы и объекты", "общее" },
            Lesson = "Самая маленькая оконная программа: окно — это класс, а кнопка и надпись — его элементы.",
            TryIt = "Нажмите кнопку «Поздороваться».",
            Steps =
            {
                "Окно — это класс Form1, унаследованный от Form: все свойства окна (Text, ClientSize) уже есть.",
                "button1 и label1 — объекты. Мы объявили их полями класса и создали в конструкторе через new.",
                "Событие Click — «кнопку нажали». Подписка button1.Click += button1_Click говорит: при нажатии вызови этот метод.",
                "В обработчике мы меняем свойство Text у label1 — надпись на экране меняется сразу.",
            },
            Form = new FormSpec
            {
                Title = "Привет, окно", Width = 360, Height = 150,
                Controls =
                {
                    Label("label1", "Нажмите на кнопку", 24, 24),
                    Button("button1", "Поздороваться", 24, 62, 150, Click("button1_Click", SetText("label1", "Привет, мир!"))),
                },
            },
        },

        new()
        {
            Id = "name", Title = "Приветствие по имени", Tags = new[] { "строки", "переменные", "условия", "типы данных" },
            Lesson = "Читаем текст из поля ввода (свойство Text), проверяем, что он не пустой, и склеиваем строку.",
            TryIt = "Сначала нажмите кнопку с пустым полем, потом введите имя.",
            Steps =
            {
                "textBox1.Text — то, что пользователь ввёл. Это обычная строка (string).",
                "string.IsNullOrWhiteSpace(...) проверяет, что пользователь что-то ввёл, а не оставил поле пустым.",
                "if / else выбирает одну из двух веток: просим ввести имя или здороваемся.",
                "\"Привет, \" + textBox1.Text + \"!\" — склейка строк оператором +.",
            },
            Form = new FormSpec
            {
                Title = "Приветствие", Width = 380, Height = 190,
                Controls =
                {
                    Label("label1", "Как вас зовут?", 24, 20),
                    Box("textBox1", 24, 46, 200),
                    Button("button1", "Поздороваться", 24, 84, 150, Click("button1_Click",
                        If("textBox1.IsEmpty",
                            new[] { SetText("label2", "Введите имя!") },
                            new[] { SetText("label2", "Привет, {textBox1}!") }))),
                    Label("label2", "", 24, 130),
                },
            },
        },

        new()
        {
            Id = "counter", Title = "Счётчик нажатий", Tags = new[] { "переменные", "операторы", "методы и return", "общее" },
            Lesson = "Переменная-поле хранит число между нажатиями; ++ увеличивает его на единицу.",
            TryIt = "Понажимайте «+1», потом «Сбросить».",
            Steps =
            {
                "private int count — поле класса. Оно живёт, пока живёт окно, и помнит значение между нажатиями.",
                "count++ — то же, что count = count + 1: увеличить на единицу.",
                "\"Нажатий: \" + count — число автоматически превращается в текст при склейке со строкой.",
                "count = 0 — присваиваем новое значение, счётчик сброшен.",
            },
            Form = new FormSpec
            {
                Title = "Счётчик", Width = 340, Height = 170,
                State = { new StateVar { Name = "count", Type = "int", Initial = "0" } },
                Controls =
                {
                    Label("label1", "Нажатий: 0", 24, 24),
                    Button("button1", "+1", 24, 64, 90, Click("button1_Click", Inc("count"), SetText("label1", "Нажатий: {count}"))),
                    Button("button2", "Сбросить", 126, 64, 100, Click("button2_Click", SetVar("count", "0"), SetText("label1", "Нажатий: {count}"))),
                },
            },
        },

        new()
        {
            Id = "calc", Title = "Сложить два числа", Tags = new[] { "операторы", "типы данных", "исключения" },
            Lesson = "TextBox даёт строку, а считать нужно числами: int.Parse превращает текст в число, try/catch ловит ошибку ввода.",
            TryIt = "Введите 7 и 5. Потом попробуйте ввести буквы.",
            Steps =
            {
                "textBox1.Text — всегда строка, даже если там цифры. Складывать строки — значит склеивать: \"7\" + \"5\" = \"75\".",
                "int.Parse(...) превращает строку в число. Тогда 7 + 5 = 12.",
                "Если ввести буквы, int.Parse бросит исключение FormatException.",
                "try { ... } catch { ... } — «попробуй, а если сломалось — сделай вот это»: программа не падает, а просит ввести числа.",
            },
            Form = new FormSpec
            {
                Title = "Сумма", Width = 380, Height = 190,
                Controls =
                {
                    Box("textBox1", 24, 24, 70, "7"),
                    Label("label1", "+", 104, 26),
                    Box("textBox2", 124, 24, 70, "5"),
                    Button("button1", "Сложить", 210, 20, 110, Click("button1_Click",
                        Try(new[] { SetText("label2", "Сумма: {=textBox1:int + textBox2:int}") },
                            new[] { SetText("label2", "Введите целые числа!") }))),
                    Label("label2", "", 24, 76),
                },
            },
        },

        new()
        {
            Id = "div", Title = "Деление и исключения", Tags = new[] { "исключения", "операторы" },
            Lesson = "Деление на ноль — классическая ошибка. try/catch превращает падение программы в понятное сообщение.",
            TryIt = "Введите 10 и 0, потом 10 и 4.",
            Steps =
            {
                "Целочисленное деление 10 / 4 даёт 2 — дробная часть отбрасывается.",
                "Деление на ноль бросает DivideByZeroException. Без try/catch программа упала бы.",
                "Блок catch выполняется только при ошибке и показывает понятное сообщение.",
            },
            Form = new FormSpec
            {
                Title = "Деление", Width = 380, Height = 190,
                Controls =
                {
                    Box("textBox1", 24, 24, 70, "10"),
                    Label("label1", "÷", 104, 26),
                    Box("textBox2", 124, 24, 70, "0"),
                    Button("button1", "Разделить", 210, 20, 110, Click("button1_Click",
                        Try(new[] { SetText("label2", "Результат: {=textBox1:int / textBox2:int}") },
                            new[] { SetText("label2", "Так нельзя: на ноль делить нельзя, и вводить нужно числа.") }))),
                    Label("label2", "", 24, 76),
                },
            },
        },

        new()
        {
            Id = "age", Title = "Условие: можно ли войти", Tags = new[] { "условия", "операторы" },
            Lesson = "Сравнение чисел операторами >=, < и выбор ветки if / else.",
            TryIt = "Введите 17, потом 18.",
            Steps =
            {
                "Возраст приходит строкой — превращаем в число через int.Parse.",
                "age >= 18 — условие. Оно даёт true или false.",
                "if выполнит первую ветку при true, else — при false.",
            },
            Form = new FormSpec
            {
                Title = "Вход с 18 лет", Width = 380, Height = 180,
                Controls =
                {
                    Label("label1", "Ваш возраст:", 24, 24),
                    Box("textBox1", 120, 22, 60, "17"),
                    Button("button1", "Проверить", 24, 64, 120, Click("button1_Click",
                        Try(new[]
                            {
                                If("textBox1:int >= 18",
                                    new[] { SetText("label2", "Добро пожаловать!") },
                                    new[] { SetText("label2", "Вход только с 18 лет.") }),
                            },
                            new[] { SetText("label2", "Введите возраст числом.") }))),
                    Label("label2", "", 24, 110),
                },
            },
        },

        new()
        {
            Id = "loop", Title = "Цикл: таблица умножения", Tags = new[] { "циклы", "массивы и индексы" },
            Lesson = "Цикл for повторяет один и тот же код для чисел от 1 до 5; ListBox показывает результат.",
            TryIt = "Нажмите кнопку несколько раз — список каждый раз собирается заново.",
            Steps =
            {
                "for (int i = 1; i <= 5; i++) — счётчик i стартует с 1 и растёт, пока не станет больше 5.",
                "Тело цикла выполняется 5 раз, каждый раз с новым i.",
                "listBox1.Items.Add(...) добавляет строку в список.",
                "listBox1.Items.Clear() очищает список перед новым заполнением.",
            },
            Form = new FormSpec
            {
                Title = "Таблица на 3", Width = 340, Height = 250,
                Controls =
                {
                    Button("button1", "Показать таблицу", 24, 20, 150, Click("button1_Click",
                        Clear("listBox1"),
                        For("i", "1", "5", Add("listBox1", "{i} × 3 = {=i * 3}")))),
                    List("listBox1", 24, 64, 280, 130),
                },
            },
        },

        new()
        {
            Id = "todo", Title = "Список дел", Tags = new[] { "коллекции", "обобщения", "массивы и индексы" },
            Lesson = "Коллекция Items хранит элементы списка; считаем их количество в переменной.",
            TryIt = "Добавьте пару дел и нажмите «Очистить».",
            Steps =
            {
                "listBox1.Items — коллекция элементов. Add добавляет, Clear удаляет всё.",
                "Пустой ввод не добавляем: проверка if перед Add.",
                "После добавления очищаем поле ввода: textBox1.Text = \"\".",
                "Переменная count считает добавленные дела.",
            },
            Form = new FormSpec
            {
                Title = "Список дел", Width = 400, Height = 280,
                State = { new StateVar { Name = "count", Type = "int", Initial = "0" } },
                Controls =
                {
                    Box("textBox1", 24, 24, 220),
                    Button("button1", "Добавить", 256, 20, 100, Click("button1_Click",
                        If("textBox1.IsEmpty",
                            new[] { Msg("Введите текст дела") },
                            new[] { Add("listBox1", "{textBox1}"), Inc("count"), SetText("textBox1", ""), SetText("label1", "Всего дел: {count}") }))),
                    List("listBox1", 24, 62, 332, 120),
                    Label("label1", "Всего дел: 0", 24, 196),
                    Button("button2", "Очистить", 256, 192, 100, Click("button2_Click", Clear("listBox1"), SetVar("count", "0"), SetText("label1", "Всего дел: {count}"))),
                },
            },
        },

        new()
        {
            Id = "check", Title = "Флажок и видимость", Tags = new[] { "типы данных", "условия", "свойства" },
            Lesson = "У CheckBox свойство Checked — это bool (true/false); у любого элемента есть свойство Visible.",
            TryIt = "Поставьте и снимите флажок.",
            Steps =
            {
                "checkBox1.Checked — значение типа bool: true, если флажок стоит.",
                "Событие CheckedChanged вызывается каждый раз, когда флажок переключили.",
                "label1.Visible = true / false показывает и скрывает надпись.",
            },
            Form = new FormSpec
            {
                Title = "Флажок", Width = 340, Height = 150,
                Controls =
                {
                    Check("checkBox1", "Показать приветствие", 24, 24, Changed("checkBox1_CheckedChanged",
                        If("checkBox1.Checked", new[] { Visible("label1", "true") }, new[] { Visible("label1", "false") }))),
                    Label("label1", "Привет! Я появился.", 24, 70, visible: false),
                },
            },
        },

        new()
        {
            Id = "picture", Title = "Картинка в окне", Tags = new[] { "файлы и потоки", "свойства", "классы и объекты" },
            Lesson = "PictureBox показывает изображение. Меняем свойство Image — меняется картинка; Visible скрывает её.",
            TryIt = "Переключайте картинки кнопками.",
            Steps =
            {
                "pictureBox1.Image = Image.FromFile(\"sun.png\") читает файл с диска и показывает его.",
                "Файл должен лежать рядом с exe (или укажите полный путь).",
                "SizeMode = Zoom масштабирует картинку под размер рамки без искажений.",
                "pictureBox1.Visible = !pictureBox1.Visible — переключатель «показать/скрыть».",
            },
            Form = new FormSpec
            {
                Title = "Картинки", Width = 420, Height = 260,
                Controls =
                {
                    Picture("pictureBox1", "sun", 24, 20, 230, 170),
                    Button("button1", "Солнце", 270, 20, 120, Click("button1_Click", Pic("pictureBox1", "sun"))),
                    Button("button2", "Луна", 270, 62, 120, Click("button2_Click", Pic("pictureBox1", "moon"))),
                    Button("button3", "Показать/скрыть", 270, 104, 120, Click("button3_Click", Visible("pictureBox1", "toggle"))),
                },
            },
        },

        new()
        {
            Id = "hero", Title = "Объект: здоровье игрока", Tags = new[] { "классы и объекты", "конструкторы", "методы и return", "свойства", "ООП и наследование" },
            Lesson = "Состояние объекта (здоровье) хранится в поле, а кнопки — это «методы»: Ударить и Лечить.",
            TryIt = "Бейте игрока до нуля и лечите его.",
            Steps =
            {
                "hp — поле объекта «игрок»: его состояние.",
                "Кнопки вызывают методы, которые меняют состояние: урон уменьшает hp, лечение — увеличивает.",
                "Условия защищают объект от неправильных значений: здоровье не уходит ниже нуля и выше 100.",
                "ProgressBar.Value показывает состояние наглядно.",
            },
            Form = new FormSpec
            {
                Title = "Игрок", Width = 380, Height = 190,
                State = { new StateVar { Name = "hp", Type = "int", Initial = "100" } },
                Controls =
                {
                    Label("label1", "Здоровье: 100", 24, 20),
                    Progress("progressBar1", 24, 48, 320, 100),
                    Button("button1", "Ударить (-15)", 24, 90, 150, Click("button1_Click",
                        If("hp <= 15",
                            new[] { SetVar("hp", "0"), Msg("Игрок погиб!") },
                            new[] { SetVar("hp", "{=hp - 15}") }),
                        Prog("progressBar1", "{hp}"), SetText("label1", "Здоровье: {hp}"))),
                    Button("button2", "Лечить (+10)", 190, 90, 150, Click("button2_Click",
                        If("hp >= 90",
                            new[] { SetVar("hp", "100") },
                            new[] { SetVar("hp", "{=hp + 10}") }),
                        Prog("progressBar1", "{hp}"), SetText("label1", "Здоровье: {hp}"))),
                },
            },
        },

        new()
        {
            Id = "events", Title = "Событие и два обработчика", Tags = new[] { "события", "делегаты и лямбды", "интерфейсы" },
            Lesson = "На одно событие можно подписать несколько методов — они выполнятся по очереди.",
            TryIt = "Нажмите кнопку несколько раз и посмотрите, в каком порядке срабатывают обработчики.",
            Steps =
            {
                "button1.Click += Первый; button1.Click += Второй; — две подписки на одно событие.",
                "При нажатии вызываются оба метода в порядке подписки.",
                "Событие — это список методов-подписчиков (делегат), который вызывается целиком.",
            },
            Form = new FormSpec
            {
                Title = "События", Width = 360, Height = 260,
                Controls =
                {
                    new ControlSpec
                    {
                        Name = "button1", Kind = ControlKind.Button, Text = "Нажми меня", X = 24, Y = 20, W = 140, H = 32,
                        Handlers =
                        {
                            Click("firstHandler", Add("listBox1", "Сработал первый обработчик")),
                            Click("secondHandler", Add("listBox1", "Сработал второй обработчик")),
                        },
                    },
                    List("listBox1", 24, 64, 300, 140),
                },
            },
        },

        new()
        {
            Id = "time", Title = "Который час?", Tags = new[] { "дата и время" },
            Lesson = "DateTime.Now — текущее время; ToString(\"HH:mm:ss\") превращает его в текст нужного вида.",
            TryIt = "Нажмите кнопку дважды с паузой.",
            Steps =
            {
                "DateTime.Now — структура с текущей датой и временем.",
                "ToString(\"HH:mm:ss\") — формат: часы, минуты, секунды.",
            },
            Form = new FormSpec
            {
                Title = "Часы", Width = 320, Height = 140,
                Controls =
                {
                    Button("button1", "Который час?", 24, 20, 140, Click("button1_Click", SetText("label1", "Сейчас {now:HH:mm:ss}"))),
                    Label("label1", "", 24, 70),
                },
            },
        },

        new()
        {
            Id = "strings", Title = "Работа со строкой", Tags = new[] { "строки" },
            Lesson = "Методы ToUpper, ToLower и свойство Length — основа работы со строками.",
            TryIt = "Введите любое слово и нажимайте кнопки.",
            Steps =
            {
                "ToUpper() и ToLower() возвращают НОВУЮ строку — исходная не меняется (строки неизменяемы).",
                "Length — количество символов в строке.",
            },
            Form = new FormSpec
            {
                Title = "Строки", Width = 400, Height = 190,
                Controls =
                {
                    Box("textBox1", 24, 22, 240, "Привет, C#"),
                    Button("button1", "ЗАГЛАВНЫЕ", 24, 60, 110, Click("button1_Click", SetText("label1", "{textBox1:upper}"))),
                    Button("button2", "строчные", 142, 60, 110, Click("button2_Click", SetText("label1", "{textBox1:lower}"))),
                    Button("button3", "Длина", 260, 60, 90, Click("button3_Click", SetText("label1", "Символов: {textBox1:len}"))),
                    Label("label1", "", 24, 112),
                },
            },
        },
    };

    public static FormExample ById(string id) => All.First(e => e.Id == id);

    /// <summary>Какие оконные примеры подходят уроку: по тегам урока; если ничего — два самых простых.</summary>
    public static List<FormExample> ForTags(IEnumerable<string> lessonTags)
    {
        var tags = lessonTags.ToHashSet();
        var picked = All
            .Select((e, i) => (e, score: e.Tags.Count(t => tags.Contains(t) && t != "общее"), i))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score).ThenBy(x => x.i)
            .Select(x => x.e)
            .Take(3)
            .ToList();

        if (picked.Count == 0) picked = new List<FormExample> { ById("hello"), ById("counter") };
        return picked;
    }
}
