using static CsharpLearning.Core.Pedagogy.Curriculum;

namespace CsharpLearning.Core.Pedagogy;

/// <summary>
/// Учебный план второго курса — «Rust для самых зелёных» по учебнику metanit.com/rust.
/// API такой же, как у Curriculum: ступени, хвостовая ступень, IsAuthored и Locate.
/// </summary>
public static class RustCurriculum
{
    /// <summary>Номер «хвостовой» ступени для уроков, которых нет в плане.</summary>
    public const int ExtraStageNumber = 99;

    static string[] S(params string[] slugs) => slugs;

    public static readonly IReadOnlyList<Stage> Stages = new Stage[]
    {
        new(1, "Знакомство: что такое Rust", "Новичок", S(
            "own-rust-what", "1.1.php", "own-rust-tools", "1.2.php", "own-rust-first", "2.1.php")),

        new(2, "Данные: переменные и типы", "Новичок", S(
            "own-rust-variables", "2.2.php", "2.13.php", "own-rust-types", "2.3.php", "2.26.php",
            "2.4.php", "2.5.php", "11.1.php")),

        new(3, "Решения и повторения", "Новичок", S(
            "2.6.php", "2.7.php", "2.8.php", "2.9.php")),

        new(4, "Функции и замыкания", "Начальный", S(
            "2.10.php", "2.11.php", "2.12.php", "2.20.php", "2.21.php", "2.23.php", "2.24.php")),

        new(5, "Составные данные: кортежи, структуры, перечисления", "Начальный", S(
            "2.14.php", "2.15.php", "2.16.php", "2.17.php", "2.18.php", "2.19.php", "2.25.php", "2.22.php")),

        new(6, "Владение и заимствование", "Начальный", S(
            "3.1.php", "3.2.php", "own-rust-ownership", "3.3.php", "own-rust-borrowing", "3.4.php",
            "3.5.php", "3.7.php", "own-rust-errors")),

        new(7, "Строки и коллекции", "Основы", S(
            "7.1.php", "7.2.php", "3.6.php", "7.3.php", "7.4.php", "5.10.php", "7.5.php")),

        new(8, "Ошибки во время работы", "Основы", S(
            "own-rust-option-result", "10.1.php", "10.2.php", "10.3.php", "10.4.php", "10.5.php")),

        new(9, "Методы и трейты", "Средний", S(
            "5.1.php", "5.2.php", "5.15.php", "5.3.php", "5.4.php", "5.8.php", "5.9.php")),

        new(10, "Обобщения и объекты трейтов", "Средний", S(
            "5.5.php", "5.6.php", "5.7.php", "5.13.php", "5.11.php", "5.12.php", "5.14.php")),

        new(11, "Времена жизни", "Средний", S(
            "6.1.php", "6.2.php", "6.3.php", "6.4.php", "6.5.php")),

        new(12, "Модули, Cargo и крейты", "Средний", S(
            "8.1.php", "8.2.php", "8.3.php", "8.4.php", "9.1.php", "9.2.php", "9.3.php")),

        new(13, "Файлы и тестирование", "Выше среднего", S(
            "14.1.php", "14.2.php", "15.1.php", "15.2.php", "15.3.php", "15.4.php")),

        new(14, "Умные указатели и потоки", "Продвинутый", S(
            "12.2.php", "13.1.php", "13.2.php", "13.3.php", "13.4.php", "13.5.php", "13.6.php",
            "13.7.php", "13.8.php", "13.9.php", "13.10.php")),

        new(15, "Под капотом: unsafe, макросы, связь с C/C++", "Эксперт", S(
            "12.1.php", "16.1.php", "17.1.php", "17.2.php", "17.3.php")),
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
