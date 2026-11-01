using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CsharpLearning.Core.Forms;
using CsharpLearning.Core.Model;
using CsharpLearning.GraphEditor;

namespace CsharpLearning.App.View;

/// <summary>
/// Вкладка «Окно»: тот же материал урока, но на Windows Forms. Живое окно, настоящий код рядом
/// и разбор по шагам — что происходит при нажатии кнопки.
/// </summary>
public sealed class WindowExamplesView : ScrollViewer
{
    readonly StackPanel _root = new() { Margin = new Thickness(22, 18, 22, 28) };
    List<FormExample> _examples = new();
    FormExample? _current;
    string? _lessonKey;

    static Brush B(string hex) => CodeRender.Solid(hex);

    public WindowExamplesView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _root;
    }

    public void SetLesson(Lesson? lesson)
    {
        string key = lesson?.Slug ?? "";
        if (key == _lessonKey && _examples.Count > 0) return;
        _lessonKey = key;

        _examples = FormExamples.ForTags(lesson?.Tags ?? new List<string>());
        Show(_examples[0]);
    }

    void Show(FormExample ex)
    {
        _current = ex;
        _root.Children.Clear();

        _root.Children.Add(new TextBlock { Text = "Оконное приложение (Windows Forms)", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = B("#FFE9ECF2") });
        _root.Children.Add(Muted("Те же идеи урока, но не в чёрной консоли, а в окне: кнопки, поля ввода, надписи. Окно ниже настоящее — нажимайте и вводите.", 6));

        // выбор примера
        if (_examples.Count > 1)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var e in _examples)
            {
                var item = e;
                bool on = item == ex;
                var chip = new Border
                {
                    Child = new TextBlock { Text = item.Title, FontSize = 13, Foreground = B(on ? "#FFFFFFFF" : "#FFB6BFD1") },
                    Background = B(on ? "#FF2C4A63" : "#FF2E3341"), BorderBrush = B(on ? "#FF56B4E9" : "#FF3A4152"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                };
                chip.MouseLeftButtonUp += (s, a) => Show(item);
                chips.Children.Add(chip);
            }
            _root.Children.Add(chips);
        }

        // карточка «чему учит»
        var card = new Border
        {
            Background = B("#FF272B36"), BorderBrush = B("#FF3A4152"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 10, 0, 14),
        };
        var cardBody = new StackPanel();
        cardBody.Children.Add(new TextBlock { Text = ex.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = B("#FF56B4E9") });
        cardBody.Children.Add(Muted("Чему учит: " + ex.Lesson, 4, "#FFD0D6E4"));
        if (ex.TryIt.Length > 0) cardBody.Children.Add(Muted("Попробуйте: " + ex.TryIt, 4, "#FF9AA4B8"));
        card.Child = cardBody;
        _root.Children.Add(card);

        // окно + код
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 320 });

        var rt = new FormRuntime(ex.Form);
        var left = new StackPanel();
        left.Children.Add(new FormPreview(rt));
        var reset = new Button { Content = "↺ Сбросить окно", Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(14, 0, 14, 0) };
        reset.SetResourceReference(StyleProperty, "FlatButton");
        reset.Click += (s, e) => rt.Reset();
        left.Children.Add(reset);
        grid.Children.Add(left);

        string code = WinFormsCodeGen.Generate(ex.Form);
        var codeBlock = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 12.5, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = B("#FFDCDCDC"),
        };
        CodeRender.Fill(codeBlock, code);

        var codeHead = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var copy = new Button { Content = "Копировать код", Padding = new Thickness(12, 0, 12, 0) };
        copy.SetResourceReference(StyleProperty, "FlatButton");
        copy.Click += (s, e) => { Clipboard.SetText(code); copy.Content = "Скопировано ✓"; };
        DockPanel.SetDock(copy, Dock.Right);
        codeHead.Children.Add(copy);
        codeHead.Children.Add(new TextBlock { Text = "Код Form1.cs", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = B("#FFE9ECF2"), VerticalAlignment = VerticalAlignment.Center });

        var right = new StackPanel();
        right.Children.Add(codeHead);
        right.Children.Add(new Border
        {
            Background = B("#FF14161D"), BorderBrush = B("#FF3A4152"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = codeBlock },
        });
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        _root.Children.Add(grid);

        // разбор
        _root.Children.Add(new TextBlock { Text = "Как это работает", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = B("#FF56B4E9"), Margin = new Thickness(0, 24, 0, 8) });
        int n = 1;
        foreach (var step in ex.Steps)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = B("#FF2C4A63"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 12, 0),
                Child = new TextBlock { Text = (n++).ToString(), Foreground = B("#FFFFFFFF"), FontWeight = FontWeights.SemiBold, FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
            row.Children.Add(new TextBlock { Text = step, Foreground = B("#FFD0D6E4"), FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxWidth = 760, LineHeight = 21 });
            _root.Children.Add(row);
        }

        _root.Children.Add(Muted("Чтобы запустить такое окно у себя: в Visual Studio создайте проект «Приложение Windows Forms (.NET)», замените содержимое Form1.cs этим кодом и нажмите F5.", 20, "#FF8C97AD"));
    }

    static TextBlock Muted(string text, double top, string color = "#FF9AA4B8") => new()
    {
        Text = text, Foreground = B(color), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), LineHeight = 20,
    };
}
