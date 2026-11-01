namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Учебный план «для самых зелёных»: уроки metanit разложены по ступеням от простого к сложному,
/// а между ними вставлены авторские вводные уроки (AuthoredLessons). Порядок здесь — единственный
/// источник правды: ContentBuilder кладёт уроки в базу именно так, поэтому приложение
/// остаётся тупым читателем и не знает ни про какие «уровни».
/// </summary>
public static class Curriculum
{
    public sealed record Stage(int Number, string Title, string Level, string[] Slugs)
    {
        public string FullTitle => $"Ступень {Number}. {Title}  ·  {Level}";
    }

    /// <summary>Номер «хвостовой» ступени для уроков, которых нет в плане (новые уроки учебника).</summary>
    public const int ExtraStageNumber = 99;

    static string[] S(params string[] slugs) => slugs;
    static string[] Range(string prefix, int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(i => $"{prefix}.{i}.php").ToArray();

    public static readonly IReadOnlyList<Stage> Stages = new Stage[]
    {
        new(1, "Знакомство: что такое программа", "Новичок", S(
            "own-program", "own-how-runs", "1.1.php", "own-first-program", "1.5.php", "1.4.php")),

        new(2, "Данные: переменные и типы", "Новичок", S(
            "own-variables", "2.25.php", "2.26.php", "own-types", "2.1.php", "2.15.php",
            "2.3.php", "2.23.php", "2.2.php", "20.4.php", "own-errors")),

        new(3, "Решения и повторения", "Новичок", S(
            "2.24.php", "2.5.php", "3.45.php", "2.12.php", "2.6.php", "2.4.php", "2.7.php")),

        new(4, "Методы: собираем код из кирпичиков", "Основы", S(
            "2.8.php", "2.9.php", "2.34.php", "2.27.php", "2.10.php", "2.18.php", "2.20.php", "2.11.php")),

        new(5, "Строки и текст", "Основы", S(
            "7.1.php", "7.2.php", "7.5.php", "7.3.php", "7.4.php")),

        new(6, "Ошибки во время работы", "Основы", S(
            "2.14.php", "2.31.php", "2.28.php", "2.29.php", "2.30.php")),

        new(7, "Классы и объекты", "Основы", S(
            "3.1.php", "3.35.php", "3.2.php", "3.4.php", "3.3.php", "3.5.php", "3.6.php",
            "2.16.php", "2.13.php", "2.17.php", "3.50.php", "3.26.php", "2.19.php")),

        new(8, "Проекты, пространства имён, пакеты", "Средний", S(
            "3.25.php", "3.52.php", "3.53.php", "4.1.php", "3.46.php", "3.61.php", "3.60.php")),

        new(9, "Наследование и полиморфизм", "Средний", S(
            "3.7.php", "3.11.php", "3.19.php", "3.41.php", "3.42.php", "3.8.php", "3.10.php", "3.17.php")),

        new(10, "Интерфейсы", "Средний", S(
            "3.9.php", "3.49.php", "3.44.php", "3.47.php", "3.48.php", "3.22.php", "3.23.php")),

        new(11, "Обобщения и коллекции", "Средний", S(
            "3.12.php", "3.38.php", "3.39.php", "3.40.php", "4.5.php", "4.9.php", "4.7.php", "4.8.php",
            "4.6.php", "20.5.php", "4.11.php", "4.12.php", "4.13.php", "2.32.php")),

        new(12, "Делегаты, события, лямбды", "Средний", S(
            "3.13.php", "3.43.php", "3.15.php", "3.16.php", "3.33.php", "3.14.php", "3.54.php", "3.28.php")),

        new(13, "LINQ: запросы к данным", "Выше среднего", S(
            "15.1.php", "13.6.php", "15.2.php", "15.3.php", "15.5.php", "15.11.php", "15.4.php",
            "15.6.php", "15.7.php", "15.10.php", "15.8.php", "15.9.php")),

        new(14, "Даты, файлы, JSON и XML", "Выше среднего", S(
            "19.1.php", "19.2.php", "19.3.php", "20.2.php", "20.1.php",
            "5.1.php", "5.2.php", "5.3.php", "5.4.php", "5.5.php", "5.6.php", "5.7.php",
            "6.5.php", "16.1.php", "16.2.php", "16.3.php", "16.4.php", "16.5.php", "16.6.php", "16.7.php", "6.4.php")),

        new(15, "Современный C#", "Продвинутый", S(
            "3.51.php", "3.20.php", "3.34.php", "3.55.php", "3.56.php", "3.57.php", "3.58.php", "3.59.php",
            "3.18.php", "3.64.php", "3.21.php", "3.36.php", "3.37.php", "3.63.php", "4.10.php", "2.21.php",
            "3.27.php", "23.1.php", "23.2.php", "23.3.php", "23.4.php")),

        new(16, "Многопоточность и асинхронность", "Продвинутый", S(
            "11.1.php", "11.2.php", "11.3.php", "11.4.php", "11.5.php", "11.10.php", "11.6.php", "11.7.php", "11.8.php",
            "12.1.php", "12.2.php", "12.3.php", "12.4.php", "12.5.php",
            "13.3.php", "13.7.php", "13.5.php", "13.4.php", "13.8.php",
            "17.1.php", "17.2.php", "17.3.php")),

        new(17, "Под капотом: память, рефлексия, процессы", "Эксперт", S(
            "8.1.php", "8.2.php", "8.5.php", "8.3.php", "8.4.php", "2.33.php", "2.22.php", "3.62.php",
            "14.1.php", "14.2.php", "14.5.php", "14.6.php", "14.3.php", "14.4.php",
            "9.1.php", "9.2.php", "9.3.php", "18.1.php", "18.2.php", "18.3.php", "24.1.php", "24.2.php")),

        new(18, "Приложение: установка инструментов", "Справочник", S(
            "own-tools", "1.3.php", "1.2.php", "1.6.php", "1.7.php")),
    };

    public static Stage ExtraStage { get; } = new(ExtraStageNumber, "Дополнительные уроки", "Разное", Array.Empty<string>());

    public static bool IsAuthored(string slug) => slug.StartsWith("own-", StringComparison.Ordinal);

    /// <summary>Какая ступень и какой по счёту урок в ней. null — урока нет в плане.</summary>
    public static (Stage stage, int position)? Locate(string slug)
    {
        foreach (var st in Stages)
        {
            int i = Array.IndexOf(st.Slugs, slug);
            if (i >= 0) return (st, i);
        }
        return null;
    }
}
