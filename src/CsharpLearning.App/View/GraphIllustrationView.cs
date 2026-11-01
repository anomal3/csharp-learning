using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CsharpLearning.Core.Graph;
using CsharpLearning.Core.Model;
using CsharpLearning.GraphEditor;

namespace CsharpLearning.App.View;

/// <summary>
/// Вкладка «Граф»: та же идея урока, собранная из нод. Граф можно рассматривать (двигать и
/// увеличивать), справа — получившийся код, а кнопка открывает пример в полном редакторе.
/// </summary>
public sealed class GraphIllustrationView : ScrollViewer
{
    readonly StackPanel _root = new() { Margin = new Thickness(22, 18, 22, 28) };
    readonly NodeCanvas _canvas = new() { ReadOnly = true, MinFitScale = 0.4, Height = 300, MinWidth = 360 };
    readonly TextBlock _code = new() { FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 12.5, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = Solid("#FFDCDCDC") };
    readonly Dictionary<int, GraphProject> _projects = new();
    List<int> _samples = new();
    int _sample;
    (string cls, string method) _method;
    string? _lessonKey;

    static Brush Solid(string hex) => CodeRender.Solid(hex);

    public GraphIllustrationView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Content = _root;
    }

    /// <summary>Какие примеры графов подходят уроку по его тегам.</summary>
    static List<int> SamplesFor(IEnumerable<string> tags)
    {
        var t = tags.ToHashSet();
        var list = new List<int>();
        void Add(int i) { if (!list.Contains(i)) list.Add(i); }

        if (t.Contains("классы и объекты") || t.Contains("конструкторы") || t.Contains("свойства") || t.Contains("ООП и наследование")) Add(3);
        if (t.Contains("методы и return")) Add(4);
        if (t.Contains("циклы") || t.Contains("массивы и индексы") || t.Contains("коллекции")) Add(2);
        if (t.Contains("условия") || t.Contains("операторы") || t.Contains("типы данных") || t.Contains("переменные")) Add(1);
        if (list.Count == 0 || t.Contains("старт и тулчейн") || t.Contains("программа и Main")) Add(0);
        return list.Take(3).ToList();
    }

    public void SetLesson(Lesson? lesson)
    {
        string key = lesson?.Slug ?? "";
        if (key == _lessonKey && _samples.Count > 0) return;
        _lessonKey = key;

        _samples = SamplesFor(lesson?.Tags ?? new List<string>());
        ShowSample(_samples[0]);
    }

    GraphProject Project(int index)
    {
        if (!_projects.TryGetValue(index, out var p))
        {
            p = GraphSamples.All[index].Build();
            AutoLayout.ArrangeAll(p);
            _projects[index] = p;
        }
        return p;
    }

    void ShowSample(int index)
    {
        _sample = index;
        var project = Project(index);
        var main = project.FindClass(project.EntryClass)!;
        _method = (main.Name, project.EntryMethod);
        Build();
    }

    void Build()
    {
        var project = Project(_sample);
        var sample = GraphSamples.All[_sample];
        _root.Children.Clear();

        _root.Children.Add(new TextBlock { Text = "Граф: та же программа из нод", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Solid("#FFE9ECF2") });
        _root.Children.Add(Muted("Слева — программа как схема: белые стрелки задают порядок действий, цветные кружки несут данные. Справа — обычный код C#, который из неё получается. Колесо мыши — масштаб, правая кнопка — двигать вид.", 6));

        // выбор примера
        if (_samples.Count > 1)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var i in _samples)
            {
                int idx = i;
                bool on = idx == _sample;
                var chip = new Border
                {
                    Child = new TextBlock { Text = GraphSamples.All[idx].Title, FontSize = 13, Foreground = Solid(on ? "#FFFFFFFF" : "#FFB6BFD1") },
                    Background = Solid(on ? "#FF2C4A63" : "#FF2E3341"), BorderBrush = Solid(on ? "#FF56B4E9" : "#FF3A4152"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 8, 8), Cursor = Cursors.Hand,
                };
                chip.MouseLeftButtonUp += (s, e) => ShowSample(idx);
                chips.Children.Add(chip);
            }
            _root.Children.Add(chips);
        }

        var card = new Border
        {
            Background = Solid("#FF272B36"), BorderBrush = Solid("#FF3A4152"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 10, 0, 14),
        };
        var body = new DockPanel();
        var open = new Button { Content = "Открыть в редакторе", Padding = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        open.SetResourceReference(StyleProperty, "PrimaryButton");
        open.Click += (s, e) =>
        {
            var copy = GraphJson.Clone(project);
            new GraphEditorWindow(copy) { Owner = Window.GetWindow(this) }.Show();
        };
        DockPanel.SetDock(open, Dock.Right);
        body.Children.Add(open);
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = sample.Title, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Solid("#FF56B4E9") });
        texts.Children.Add(Muted(sample.Description, 4, "#FFD0D6E4"));
        body.Children.Add(texts);
        card.Child = body;
        _root.Children.Add(card);

        // методы проекта
        var methods = project.Classes.SelectMany(c => c.Methods.Select(m => (c.Name, m))).ToList();
        if (methods.Count > 1)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            chips.Children.Add(new TextBlock { Text = "Граф метода:", Foreground = Solid("#FF8C97AD"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 8) });
            foreach (var (cn, m) in methods)
            {
                var cls = cn; var mm = m;
                bool on = _method == (cls, mm.Name);
                var chip = new Border
                {
                    Child = new TextBlock { Text = mm.IsCtor ? cls + " (конструктор)" : $"{cls}.{mm.Name}", FontSize = 12.5, Foreground = Solid(on ? "#FFFFFFFF" : "#FFB6BFD1") },
                    Background = Solid(on ? "#FF2C4A63" : "#FF2E3341"), BorderBrush = Solid(on ? "#FF56B4E9" : "#FF3A4152"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 8), Cursor = Cursors.Hand,
                };
                chip.MouseLeftButtonUp += (s, e) => { _method = (cls, mm.Name); Build(); };
                chips.Children.Add(chip);
            }
            _root.Children.Add(chips);
        }

        // граф + код
        // граф на всю ширину (цепочки нод длинные), получившийся код — под ним
        var grid = new StackPanel();

        if (_canvas.Parent is Border oldHost) oldHost.Child = null;
        var cv = new Border { BorderBrush = Solid("#FF3A4152"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), ClipToBounds = true, Child = _canvas, Margin = new Thickness(0, 0, 0, 16) };
        grid.Children.Add(cv);

        var gen = GraphCodeGen.Generate(project, withUsings: true);
        CodeRender.Fill(_code, gen.Code);
        var codeBox = new Border
        {
            Background = Solid("#FF14161D"), BorderBrush = Solid("#FF3A4152"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12),
            Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _code },
        };
        grid.Children.Add(new TextBlock { Text = "Получившийся код C#", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Solid("#FFE9ECF2"), Margin = new Thickness(0, 0, 0, 8) });
        grid.Children.Add(codeBox);
        _root.Children.Add(grid);

        // привязываем после размещения: холсту нужен размер, чтобы вписать граф
        if (project.FindClass(_method.cls) is { } c0 && c0.FindMethod(_method.method) is { } m0)
        {
            _canvas.Bind(project, c0, m0);
            Dispatcher.BeginInvoke(() => _canvas.FitAll(), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    static TextBlock Muted(string text, double top, string color = "#FF9AA4B8") => new()
    {
        Text = text, Foreground = Solid(color), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), LineHeight = 20,
    };
}
