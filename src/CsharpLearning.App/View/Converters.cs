using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;

namespace CsharpLearning.App.View;

/// <summary>true → видно, false → скрыто.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>Инверсия: false → видно.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

public sealed class BoolToStringConverter : IValueConverter
{
    public string WhenTrue { get; set; } = "";
    public string WhenFalse { get; set; } = "";

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? WhenTrue : WhenFalse;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, WhenTrue, StringComparison.Ordinal);
}

/// <summary>Инверсия bool: поле ответа отключается, когда ответ уже дан.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && !b;
}

/// <summary>Верно/неверно → цвет ответа.</summary>
public sealed class CorrectToBrushConverter : IValueConverter
{
    static readonly Brush Good = Freeze("#FF63C77A");
    static readonly Brush Bad = Freeze("#FFE4695F");

    static Brush Freeze(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? Good : Bad;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Health темы → цвет метки в аналитике.</summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    static readonly Brush Good = Freeze("#FF63C77A");
    static readonly Brush Normal = Freeze("#FF9AA4B8");
    static readonly Brush Struggling = Freeze("#FFE4695F");
    static readonly Brush NotRead = Freeze("#FF5A6274");

    static Brush Freeze(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            CsharpLearning.Core.Analytics.Health.Good => Good,
            CsharpLearning.Core.Analytics.Health.Struggling => Struggling,
            CsharpLearning.Core.Analytics.Health.Normal => Normal,
            _ => NotRead,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Собственная подсветка кода (AvalonEdit для net10.0 не подходит — пакет помечен как
/// .NET Framework-only, NuGet отдаёт NU1701). Токенизатор простой, но честный:
/// ключевые слова, строки, числа, комментарии и типы получают разные цвета.
/// </summary>
public sealed class CodeHighlightConverter : IValueConverter
{
    static readonly Brush Keyword = Freeze("#FF6FB3E8");
    static readonly Brush TypeName = Freeze("#FF8FD0B8");
    static readonly Brush StringLit = Freeze("#FFC9C06B");
    static readonly Brush Number = Freeze("#FFE8A06B");
    static readonly Brush Comment = Freeze("#FF7A879C");
    static readonly Brush Plain = Freeze("#FFE4E8EF");

    static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
        "continue","decimal","default","delegate","do","double","dynamic","else","enum","event","explicit",
        "extern","false","float","for","foreach","get","global","if","implicit","in","int","interface",
        "internal","is","lock","long","namespace","new","null","operator","out","override","params","partial",
        "private","protected","public","readonly","ref","return","sbyte","sealed","set","short","sizeof",
        "static","string","struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked",
        "unsafe","ushort","using","var","virtual","void","volatile","while","yield","async","await",
        "record","init","required","nameof","value","stackalloc","fixed","add","remove","when",
    };

    static readonly Regex Token = new(
        @"(?<comment>//[^\n]*)|(?<comment>/\*.*?\*/)|(?<verbatim>@?""(?:\\.|[^""\\])*"")|(?<charlit>'(?:\\.|[^'\\])')|(?<number>\b\d[\d_]*(?:\.\d[\d_]*)?[uUlLfFdDmM]*\b)|(?<word>[_@A-Za-z][_A-Za-z0-9]*)",
        RegexOptions.Compiled | RegexOptions.Singleline);

    static Brush Freeze(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string code = value as string ?? "";
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
            FontSize = 13,
            PagePadding = new Thickness(0),
            Background = Brushes.Transparent,
            TextAlignment = TextAlignment.Left,
        };

        foreach (var rawLine in code.Replace("\r\n", "\n").Split('\n'))
        {
            var p = new Paragraph { Margin = new Thickness(0), LineHeight = 19 };
            foreach (var run in Colorize(rawLine)) p.Inlines.Add(run);
            if (rawLine.Length == 0) p.Inlines.Add(new Run(" ") { Foreground = Plain });
            doc.Blocks.Add(p);
        }
        return doc;
    }

    internal static IEnumerable<Inline> Colorize(string line)
    {
        int pos = 0;
        foreach (Match m in Token.Matches(line))
        {
            if (m.Index > pos)
                yield return new Run(line.Substring(pos, m.Index - pos)) { Foreground = Plain };

            var text = m.Value;
            Brush brush =
                m.Groups["comment"].Success ? Comment :
                m.Groups["verbatim"].Success ? StringLit :
                m.Groups["charlit"].Success ? StringLit :
                m.Groups["number"].Success ? Number :
                Keywords.Contains(text) ? Keyword :
                LooksLikeType(text) ? TypeName :
                Plain;

            yield return new Run(text) { Foreground = brush };
            pos = m.Index + text.Length;
        }

        if (pos < line.Length)
            yield return new Run(line.Substring(pos)) { Foreground = Plain };
    }

    static bool LooksLikeType(string w) =>
        w.Length > 1 && char.IsUpper(w[0]) && !Keywords.Contains(w);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Подсвеченный код в обычном TextBlock: высота ровно по строкам, без прокрутки-в-прокрутке.
/// Использование: view:CodeView.Code="{Binding Code}".
/// </summary>
public static class CodeView
{
    public static readonly DependencyProperty CodeProperty = DependencyProperty.RegisterAttached(
        "Code", typeof(string), typeof(CodeView), new PropertyMetadata("", OnCodeChanged));

    public static string GetCode(DependencyObject d) => (string)d.GetValue(CodeProperty);
    public static void SetCode(DependencyObject d, string v) => d.SetValue(CodeProperty, v);

    // Палитра Visual Studio (тёмная тема)
    static readonly Dictionary<CsharpLearning.Core.Runner.CodeKind, Brush> Palette = new()
    {
        [CsharpLearning.Core.Runner.CodeKind.Plain] = Solid("#FFDCDCDC"),
        [CsharpLearning.Core.Runner.CodeKind.Keyword] = Solid("#FF569CD6"),
        [CsharpLearning.Core.Runner.CodeKind.Control] = Solid("#FFD8A0DF"),
        [CsharpLearning.Core.Runner.CodeKind.Type] = Solid("#FF4EC9B0"),
        [CsharpLearning.Core.Runner.CodeKind.Method] = Solid("#FFDCDCAA"),
        [CsharpLearning.Core.Runner.CodeKind.Local] = Solid("#FF9CDCFE"),
        [CsharpLearning.Core.Runner.CodeKind.String] = Solid("#FFD69D85"),
        [CsharpLearning.Core.Runner.CodeKind.Number] = Solid("#FFB5CEA8"),
        [CsharpLearning.Core.Runner.CodeKind.Comment] = Solid("#FF57A64A"),
        [CsharpLearning.Core.Runner.CodeKind.Operator] = Solid("#FFB4B4B4"),
    };

    static Brush Solid(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static void OnCodeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        tb.Inlines.Clear();
        string code = (e.NewValue as string ?? "").Replace("\r\n", "\n").Replace("\t", "    ").TrimEnd('\n');
        if (code.Length == 0) return;

        int pos = 0;
        foreach (var span in CsharpLearning.Core.Runner.CodeClassifier.Classify(code))
        {
            if (span.Start < pos || span.Start + span.Length > code.Length) continue;
            if (span.Start > pos) Append(tb, code.Substring(pos, span.Start - pos), Palette[CsharpLearning.Core.Runner.CodeKind.Plain]);
            Append(tb, code.Substring(span.Start, span.Length), Palette[span.Kind]);
            pos = span.Start + span.Length;
        }
        if (pos < code.Length) Append(tb, code.Substring(pos), Palette[CsharpLearning.Core.Runner.CodeKind.Plain]);
    }

    /// <summary>Добавляет текст, превращая переводы строк в LineBreak.</summary>
    static void Append(System.Windows.Controls.TextBlock tb, string text, Brush brush)
    {
        var lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) tb.Inlines.Add(new LineBreak());
            if (lines[i].Length > 0) tb.Inlines.Add(new Run(lines[i]) { Foreground = brush });
        }
    }
}

/// <summary>
/// Абзац со словами-подсказками: подчёркнутые пунктиром слова при наведении мыши
/// показывают бытовую аналогию (всплывающая карточка).
/// </summary>
public static class RichText
{
    public static readonly DependencyProperty BlockProperty = DependencyProperty.RegisterAttached(
        "Block", typeof(BlockVm), typeof(RichText), new PropertyMetadata(null, OnBlockChanged));

    public static BlockVm? GetBlock(DependencyObject d) => (BlockVm?)d.GetValue(BlockProperty);
    public static void SetBlock(DependencyObject d, BlockVm? v) => d.SetValue(BlockProperty, v);

    static readonly Brush Accent = Frozen("#FF56B4E9");
    static readonly Brush CardBg = Frozen("#FF22283A");
    static readonly Brush Muted = Frozen("#FF9AA4B8");
    static readonly Brush Light = Frozen("#FFE9ECF2");
    static readonly Brush Good = Frozen("#FF63C77A");

    static Brush Frozen(string hex)
    {
        var b = (Brush)new BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    static void OnBlockChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not BlockVm block) return;

        string text = block.Text;
        int pos = 0;
        foreach (var hit in block.Hits.OrderBy(h => h.Start))
        {
            if (hit.Start > pos) tb.Inlines.Add(new Run(text.Substring(pos, hit.Start - pos)));

            var underline = new TextDecoration
            {
                Location = TextDecorationLocation.Underline,
                Pen = new Pen(Accent, 1.2) { DashStyle = DashStyles.Dot },
                PenOffset = 2,
            };
            var run = new Run(text.Substring(hit.Start, hit.Length))
            {
                Foreground = Accent,
                TextDecorations = new TextDecorationCollection { underline },
                Cursor = System.Windows.Input.Cursors.Help,
                ToolTip = BuildTip(hit),
            };
            System.Windows.Controls.ToolTipService.SetInitialShowDelay(run, 120);
            System.Windows.Controls.ToolTipService.SetShowDuration(run, 60000);
            tb.Inlines.Add(run);
            pos = hit.Start + hit.Length;
        }
        if (pos < text.Length) tb.Inlines.Add(new Run(text.Substring(pos)));
    }

    static System.Windows.Controls.ToolTip BuildTip(GlossaryHit h)
    {
        System.Windows.Controls.TextBlock Line(string label, string body, Brush labelBrush)
        {
            var t = new System.Windows.Controls.TextBlock
            {
                TextWrapping = TextWrapping.Wrap, MaxWidth = 400, Margin = new Thickness(0, 7, 0, 0),
                Foreground = Light, FontSize = 13, LineHeight = 19,
            };
            t.Inlines.Add(new Run(label + " ") { Foreground = labelBrush, FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new Run(body));
            return t;
        }

        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock
        {
            Text = h.Concept, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Accent,
        });
        panel.Children.Add(Line("Представь:", h.Animal, Muted));
        panel.Children.Add(Line("В быту:", h.Everyday, Muted));
        panel.Children.Add(Line("В коде:", h.Bridge, Good));

        return new System.Windows.Controls.ToolTip
        {
            Content = panel, Background = CardBg, BorderBrush = Accent, BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 10, 14, 12),
        };
    }
}
