using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CsharpLearning.Core.Graph;
using CsharpLearning.Core.Runner;
using Microsoft.Win32;

namespace CsharpLearning.GraphEditor;

/// <summary>
/// Редактор графов C#: слева классы проекта, в центре холст метода, справа исходный код,
/// снизу проблемы и вывод запуска. Граф → код C# → настоящая компиляция и запуск.
/// </summary>
public sealed class GraphEditorView : Grid
{
    GraphProject _p = GraphProject.CreateEmpty();
    string? _filePath;

    readonly NodeCanvas _canvas = new();
    readonly StackPanel _tree = new();
    readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal };
    readonly TextBlock _code = new() { FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"), FontSize = 13, LineHeight = 19, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Foreground = CodeRender.Solid("#FFDCDCDC"), Margin = new Thickness(12, 10, 12, 10) };
    readonly ScrollViewer _codeScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly TextBox _out = new() { IsReadOnly = true, FontFamily = new FontFamily("Consolas, Cascadia Mono"), FontSize = 12.5, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = CodeRender.Solid("#FF63C77A"), TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(10, 6, 10, 6) };
    readonly ListBox _issues = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    readonly TextBlock _status = new() { Foreground = CodeRender.Solid("#FF9AA4B8"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBox _name;
    readonly Button _runBtn;
    readonly Button _undoBtn, _redoBtn;
    readonly TextBlock _issuesTitle = new();
    readonly NodePalettePanel _palettePanel = new();
    readonly NodeInspector _inspector = new();
    readonly StackPanel _vars = new();
    readonly TextBlock _varsTitle = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = CodeRender.Solid("#FFE9ECF2"), VerticalAlignment = VerticalAlignment.Center };
    Grid _projectPanel = null!;
    Border _tabProject = null!, _tabNodes = null!;
    Border _hint = null!;
    readonly TextBlock _emptyHint = new()
    {
        Text = "Откройте метод слева (двойной щелчок) или нажмите «Примеры».", Foreground = CodeRender.Solid("#FF7C879C"),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
    };

    readonly List<(string cls, string method)> _open = new();
    (string cls, string method)? _current;
    GenResult? _lastGen;
    string? _selectedNode;
    bool _restoring;

    readonly List<string> _history = new();
    int _hi = -1;
    readonly DispatcherTimer _codeTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };

    static string AutosavePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CsharpLearning", "graph", "autosave.csgraph");

    public GraphEditorView()
    {
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CsharpLearning.GraphEditor;component/Themes/GraphDark.xaml") });
        Background = CodeRender.Solid("#FF1B1D24");
        TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
        TextElement.SetFontSize(this, 13.5);
        TextElement.SetForeground(this, CodeRender.Solid("#FFE9ECF2"));
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // ---- панель инструментов ----
        var bar = new Border { Background = CodeRender.Solid("#FF22252E"), BorderBrush = CodeRender.Solid("#FF3A4152"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 7, 10, 7) };
        var barPanel = new StackPanel { Orientation = Orientation.Horizontal };
        bar.Child = barPanel;

        _name = new TextBox
        {
            Width = 190, Height = 30, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(7, 0, 7, 0), Margin = new Thickness(0, 0, 12, 0),
            Background = CodeRender.Solid("#FF14161D"), Foreground = CodeRender.Solid("#FFE9ECF2"), BorderBrush = CodeRender.Solid("#FF3A4152"),
            CaretBrush = CodeRender.Solid("#FF56B4E9"), ToolTip = "Название проекта", FontWeight = FontWeights.SemiBold,
        };
        _name.TextChanged += (s, e) => { if (!_restoring) { _p.Name = _name.Text; ScheduleSave(); } };
        barPanel.Children.Add(_name);

        barPanel.Children.Add(Btn("Новый", NewProject));
        barPanel.Children.Add(Btn("Открыть…", OpenFile));
        barPanel.Children.Add(Btn("Сохранить", () => SaveFile(false)));
        barPanel.Children.Add(Btn("Сохранить как…", () => SaveFile(true)));
        barPanel.Children.Add(Sep());
        _undoBtn = Btn("↶ Отмена", Undo, "Ctrl+Z");
        _redoBtn = Btn("↷ Повтор", Redo, "Ctrl+Y");
        barPanel.Children.Add(_undoBtn);
        barPanel.Children.Add(_redoBtn);
        barPanel.Children.Add(Sep());
        var samples = Btn("Примеры ▾", null);
        samples.Click += (s, e) => ShowSamples(samples);
        barPanel.Children.Add(samples);
        barPanel.Children.Add(Btn("Показать всё", () => _canvas.FitAll(), "F — уместить граф в окно"));
        barPanel.Children.Add(Btn("Упорядочить", ArrangeCurrent, "Разложить ноды слева направо без наложений"));
        barPanel.Children.Add(Sep());
        _runBtn = Btn("▶ Запустить", () => _ = RunAsync(), "Скомпилировать код из графа и выполнить", primary: true);
        barPanel.Children.Add(_runBtn);
        Grid.SetRow(bar, 0);
        Children.Add(bar);

        // ---- основное поле ----
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250), MinWidth = 160 });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(440), MinWidth = 200 });

        var left = BuildLeft();
        Grid.SetColumn(left, 0);
        main.Children.Add(left);
        main.Children.Add(Splitter(1));

        var center = new Grid();
        center.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        center.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tabs, Background = CodeRender.Solid("#FF1B1D24") };
        center.Children.Add(tabScroll);
        var canvasHost = new Grid();
        canvasHost.Children.Add(_canvas);
        canvasHost.Children.Add(_emptyHint);
        _hint = BuildHint();
        canvasHost.Children.Add(_hint);
        Grid.SetRow(canvasHost, 1);
        center.Children.Add(canvasHost);
        Grid.SetColumn(center, 2);
        main.Children.Add(center);
        main.Children.Add(Splitter(3));

        var right = BuildRight();
        Grid.SetColumn(right, 4);
        main.Children.Add(right);

        Grid.SetRow(main, 1);
        Children.Add(main);

        var bottom = BuildBottom();
        Grid.SetRow(bottom, 2);
        Children.Add(bottom);

        // ---- события ----
        _canvas.Edited += () => { ScheduleCode(); ScheduleSave(); };
        _canvas.Checkpoint += PushHistory;
        _canvas.SelectionChanged += id => { _selectedNode = id; HighlightSelected(); UpdateInspector(); };
        _palettePanel.AddRequested += item => { if (_current is not null) _canvas.AddFromPalette(item); else SetStatus("Сначала откройте метод слева (вкладка «Проект»)."); };
        _codeTimer.Tick += (s, e) => { _codeTimer.Stop(); RegenerateCode(); };
        _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); Autosave(); };

        InputBindings.Add(new KeyBinding(new RelayCmd(Undo), Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCmd(Redo), Key.Y, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCmd(() => SaveFile(false)), Key.S, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCmd(() => _ = RunAsync()), Key.F5, ModifierKeys.None));

        Loaded += (s, e) => { if (_history.Count == 0) LoadInitial(); };
    }

    // ===================== построение панелей =====================
    FrameworkElement BuildLeft()
    {
        var root = new Grid { Background = CodeRender.Solid("#FF22252E") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // переключатель «Проект | Ноды»
        var tabs = new Grid { Margin = new Thickness(8, 8, 8, 4) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        _tabProject = SideTab("Проект", () => ShowLeft(true));
        _tabNodes = SideTab("Ноды", () => ShowLeft(false));
        Grid.SetColumn(_tabNodes, 1);
        tabs.Children.Add(_tabProject);
        tabs.Children.Add(_tabNodes);
        root.Children.Add(tabs);

        // ---- вкладка «Проект»: классы + переменные метода ----
        _projectPanel = new Grid();
        _projectPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _projectPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _projectPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MaxHeight = 280 });

        var head = new DockPanel { Margin = new Thickness(10, 4, 10, 6), LastChildFill = false };
        head.Children.Add(new TextBlock { Text = "Классы", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var add = Btn("+ Класс", AddClass, "Новый класс в проекте");
        add.Margin = new Thickness(0);
        DockPanel.SetDock(add, Dock.Right);
        head.Children.Add(add);
        _projectPanel.Children.Add(head);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _tree, Padding = new Thickness(6, 0, 6, 8) };
        Grid.SetRow(scroll, 1);
        _projectPanel.Children.Add(scroll);

        var varsBox = new Border { Background = CodeRender.Solid("#FF1E212A"), BorderBrush = CodeRender.Solid("#FF3A4152"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 8, 8, 8) };
        var varsGrid = new Grid();
        varsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        varsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var varsHead = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 6) };
        var addVar = Btn("+ Переменная", AddLocal, "Создать переменную текущего метода: имя, тип, начальное значение");
        addVar.Margin = new Thickness(0);
        DockPanel.SetDock(addVar, Dock.Right);
        varsHead.Children.Add(addVar);
        varsHead.Children.Add(_varsTitle);
        varsGrid.Children.Add(varsHead);
        var varsScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _vars, MaxHeight = 200 };
        Grid.SetRow(varsScroll, 1);
        varsGrid.Children.Add(varsScroll);
        varsBox.Child = varsGrid;
        Grid.SetRow(varsBox, 2);
        _projectPanel.Children.Add(varsBox);

        Grid.SetRow(_projectPanel, 1);
        root.Children.Add(_projectPanel);

        Grid.SetRow(_palettePanel, 1);
        _palettePanel.Visibility = Visibility.Collapsed;
        root.Children.Add(_palettePanel);

        ShowLeft(true);
        return root;
    }

    Border SideTab(string text, Action onClick)
    {
        var b = new Border
        {
            Child = new TextBlock { Text = text, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold, FontSize = 13.5 },
            Padding = new Thickness(0, 7, 0, 7), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(6),
        };
        b.MouseLeftButtonUp += (s, e) => onClick();
        return b;
    }

    void ShowLeft(bool project)
    {
        _projectPanel.Visibility = project ? Visibility.Visible : Visibility.Collapsed;
        _palettePanel.Visibility = project ? Visibility.Collapsed : Visibility.Visible;
        void Style(Border b, bool on)
        {
            b.Background = CodeRender.Solid(on ? "#FF2C4A63" : "#FF2A2F3D");
            ((TextBlock)b.Child).Foreground = CodeRender.Solid(on ? "#FFFFFFFF" : "#FF9AA4B8");
            b.Margin = new Thickness(Grid.GetColumn(b) == 0 ? 0 : 3, 0, Grid.GetColumn(b) == 0 ? 3 : 0, 0);
        }
        Style(_tabProject, project);
        Style(_tabNodes, !project);
    }

    FrameworkElement BuildRight()
    {
        var root = new Grid { Background = CodeRender.Solid("#FF191C24") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        root.Children.Add(_inspector);

        var head = new DockPanel { Margin = new Thickness(14, 10, 10, 7), LastChildFill = false };
        head.Children.Add(new TextBlock { Text = "Исходный код C#", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var copy = Btn("Копировать", () =>
        {
            if (_lastGen is not null) { Clipboard.SetText(_lastGen.Code); SetStatus("Код скопирован — вставьте его в Visual Studio."); }
        }, "Скопировать весь код");
        copy.Margin = new Thickness(0);
        DockPanel.SetDock(copy, Dock.Right);
        head.Children.Add(copy);
        Grid.SetRow(head, 1);
        root.Children.Add(head);

        _code.SetValue(TextBlock.IsHyphenationEnabledProperty, false);
        _codeScroll.Content = _code;
        Grid.SetRow(_codeScroll, 2);
        root.Children.Add(_codeScroll);
        return root;
    }

    /// <summary>Подсказка новичку над холстом: четыре шага. Закрывается крестиком и больше не появляется.</summary>
    Border BuildHint()
    {
        string flag = Path.Combine(Path.GetDirectoryName(AutosavePath)!, "hint.off");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Как собрать программу из нод", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
        void Step(string n, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = CodeRender.Solid("#FF56B4E9"), Margin = new Thickness(0, 0, 9, 0),
                Child = new TextBlock { Text = n, Foreground = CodeRender.Solid("#FF10131A"), FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            row.Children.Add(new TextBlock { Text = text, FontSize = 12.5, Foreground = CodeRender.Solid("#FFC5CCDA"), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        Step("1", "Зелёная нода «Начало» — отсюда стартует метод.");
        Step("2", "Вкладка «Ноды» слева: нажмите на нужную или двойной щелчок по холсту.");
        Step("3", "Соедините белые стрелки ▶ — это порядок действий; цветные кружки — данные.");
        Step("4", "Нажмите «▶ Запустить» — код справа выполнится, результат появится снизу.");

        var close = new TextBlock { Text = "✕", Foreground = CodeRender.Solid("#FF8C97AD"), FontSize = 13, Cursor = Cursors.Hand, ToolTip = "Скрыть подсказку", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var row0 = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        row0.Children.Add(close);
        row0.Children.Add(panel);

        var b = new Border
        {
            Child = row0, Background = new SolidColorBrush(Color.FromArgb(235, 31, 36, 48)), BorderBrush = CodeRender.Solid("#FF3A4152"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10), Padding = new Thickness(16, 12, 14, 12), Margin = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 560,
        };
        close.MouseLeftButtonUp += (s, e) =>
        {
            b.Visibility = Visibility.Collapsed;
            try { Directory.CreateDirectory(Path.GetDirectoryName(flag)!); File.WriteAllText(flag, "1"); } catch { }
        };
        try { if (File.Exists(flag)) b.Visibility = Visibility.Collapsed; } catch { }
        return b;
    }

    FrameworkElement BuildBottom()
    {
        var root = new Grid { Height = 170, Background = CodeRender.Solid("#FF191C24") };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var line = new Border { Height = 1, Background = CodeRender.Solid("#FF3A4152"), VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumnSpan(line, 2);
        root.Children.Add(line);

        _issuesTitle.Text = "Проблемы";
        _issuesTitle.Margin = new Thickness(12, 8, 0, 4);
        _issuesTitle.FontWeight = FontWeights.SemiBold; _issuesTitle.Foreground = CodeRender.Solid("#FFE9ECF2"); _issuesTitle.FontSize = 12.5;
        root.Children.Add(_issuesTitle);

        var outHead = new DockPanel { Margin = new Thickness(12, 8, 12, 4) };
        outHead.Children.Add(new TextBlock { Text = "Вывод программы", FontWeight = FontWeights.SemiBold, Foreground = CodeRender.Solid("#FFE9ECF2"), FontSize = 12.5 });
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(_status, Dock.Right);
        outHead.Children.Insert(0, _status);
        Grid.SetColumn(outHead, 1);
        root.Children.Add(outHead);

        _issues.ItemContainerStyle = (Style)System.Windows.Markup.XamlReader.Parse("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
                <Setter Property="Padding" Value="10,3"/>
                <Setter Property="Template">
                    <Setter.Value>
                        <ControlTemplate TargetType="ListBoxItem">
                            <Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" Padding="{TemplateBinding Padding}">
                                <ContentPresenter/>
                            </Border>
                            <ControlTemplate.Triggers>
                                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF2A3550"/></Trigger>
                            </ControlTemplate.Triggers>
                        </ControlTemplate>
                    </Setter.Value>
                </Setter>
            </Style>
            """);
        _issues.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (ItemsControl.ContainerFromElement(_issues, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: FrameworkElement { Tag: GraphIssue issue } })
                GoToIssue(issue);
        };
        Grid.SetRow(_issues, 1);
        root.Children.Add(_issues);

        var outScroll = new Border { Child = _out };
        Grid.SetRow(outScroll, 1); Grid.SetColumn(outScroll, 1);
        root.Children.Add(outScroll);
        return root;
    }

    static GridSplitter Splitter(int col)
    {
        var sp = new GridSplitter
        {
            Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch, Background = CodeRender.Solid("#FF1B1D24"),
            ResizeBehavior = GridResizeBehavior.PreviousAndNext, Cursor = Cursors.SizeWE,
        };
        Grid.SetColumn(sp, col);
        return sp;
    }

    Button Btn(string text, Action? onClick, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0), ToolTip = tip };
        b.SetResourceReference(StyleProperty, primary ? "GPrimary" : "GButton");
        if (onClick is not null) b.Click += (s, e) => onClick();
        return b;
    }

    static Border Sep() => new() { Width = 1, Margin = new Thickness(6, 3, 12, 3), Background = CodeRender.Solid("#FF3A4152") };

    sealed class RelayCmd : ICommand
    {
        readonly Action _a;
        public RelayCmd(Action a) => _a = a;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? p) => true;
        public void Execute(object? p) => _a();
    }

    // ===================== запуск и загрузка =====================
    /// <summary>Открыть сразу готовый пример (для снимков экрана и обучения): индекс в GraphSamples.All.</summary>
    public static int? InitialSample { get; set; }

    void LoadInitial()
    {
        if (InitialSample is int idx && idx >= 0 && idx < GraphSamples.All.Count)
        {
            SetProject(Arranged(GraphSamples.All[idx].Build()), null, resetHistory: true);
            return;
        }
        try
        {
            if (File.Exists(AutosavePath))
            {
                SetProject(GraphJson.Load(File.ReadAllText(AutosavePath)), null, resetHistory: true);
                return;
            }
        }
        catch { /* повреждённый автосейв — начнём с примера */ }
        SetProject(Arranged(GraphSamples.All[0].Build()), null, resetHistory: true);
    }

    static GraphProject Arranged(GraphProject p)
    {
        AutoLayout.ArrangeAll(p);
        return p;
    }

    /// <summary>Открыть готовый проект (например, пример из урока) во всём редакторе.</summary>
    public void Open(GraphProject project)
    {
        AutoLayout.ArrangeAll(project);
        SetProject(project, null, resetHistory: true);
    }

    void ArrangeCurrent()
    {
        if (CurrentMethod() is not { } cm) return;
        AutoLayout.Arrange(_p, cm.c, cm.m);
        _canvas.Bind(_p, cm.c, cm.m);
        _canvas.FitAll();
        ScheduleSave();
        PushHistory();
    }

    void SetProject(GraphProject p, string? path, bool resetHistory)
    {
        _restoring = true;
        _p = p;
        _filePath = path;
        _name.Text = p.Name;
        _restoring = false;

        _open.Clear();
        _current = null;
        if (resetHistory) { _history.Clear(); _hi = -1; }

        var entry = _p.FindClass(_p.EntryClass)?.FindMethod(_p.EntryMethod);
        RebuildTree();
        if (entry is not null) OpenMethod(_p.FindClass(_p.EntryClass)!, entry);
        else RefreshTabs();

        RegenerateCode();
        if (resetHistory) PushHistory();
    }

    // ===================== история =====================
    void PushHistory()
    {
        if (_restoring) return;
        var json = GraphJson.Save(_p);
        if (_hi >= 0 && _history[_hi] == json) return;
        if (_hi < _history.Count - 1) _history.RemoveRange(_hi + 1, _history.Count - _hi - 1);
        _history.Add(json);
        if (_history.Count > 80) _history.RemoveAt(0);
        _hi = _history.Count - 1;
        UpdateUndoButtons();
    }

    void UpdateUndoButtons()
    {
        _undoBtn.IsEnabled = _hi > 0;
        _redoBtn.IsEnabled = _hi < _history.Count - 1;
    }

    void Undo() { if (_hi > 0) RestoreHistory(--_hi); }
    void Redo() { if (_hi < _history.Count - 1) RestoreHistory(++_hi); }

    void RestoreHistory(int index)
    {
        var keep = _current;
        var keepOpen = _open.ToList();
        _restoring = true;
        _p = GraphJson.Load(_history[index]);
        _name.Text = _p.Name;
        _restoring = false;

        _open.Clear();
        foreach (var t in keepOpen) if (_p.FindClass(t.cls)?.FindMethod(t.method) is not null) _open.Add(t);
        _current = null;
        RebuildTree();

        if (keep is { } k && _p.FindClass(k.cls)?.FindMethod(k.method) is { } m) OpenMethod(_p.FindClass(k.cls)!, m);
        else
        {
            var entry = _p.FindClass(_p.EntryClass)?.FindMethod(_p.EntryMethod);
            if (entry is not null) OpenMethod(_p.FindClass(_p.EntryClass)!, entry);
            else { _canvas.Unbind(); RefreshTabs(); }
        }
        UpdateUndoButtons();
        RegenerateCode();
        ScheduleSave();
    }

    // ===================== вкладки методов =====================
    void OpenMethod(ClassDef c, MethodDef m)
    {
        var key = (c.Name, m.Name);
        if (!_open.Contains(key)) _open.Add(key);
        _current = key;
        _canvas.Bind(_p, c, m);
        _emptyHint.Visibility = Visibility.Collapsed;
        RefreshTabs();
        RebuildTree();
        ScheduleCode();
    }

    void CloseTab((string cls, string method) t)
    {
        _open.Remove(t);
        if (_current == t)
        {
            _current = null;
            var next = _open.Count > 0 ? _open[^1] : ((string, string)?)null;
            if (next is { } n && _p.FindClass(n.Item1) is { } c && c.FindMethod(n.Item2) is { } m) { OpenMethod(c, m); return; }
            _canvas.Unbind();
            _emptyHint.Visibility = Visibility.Visible;
        }
        RefreshTabs();
        RebuildTree();
    }

    void RefreshTabs()
    {
        _tabs.Children.Clear();
        foreach (var t in _open.ToList())
        {
            bool active = _current == t;
            var title = new TextBlock { Text = t.method == MethodDef.CtorName ? t.cls + " (конструктор)" : $"{t.cls}.{t.method}", Foreground = CodeRender.Solid(active ? "#FFE9ECF2" : "#FF9AA4B8"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            var close = new TextBlock { Text = "✕", Foreground = CodeRender.Solid("#FF7C879C"), FontSize = 11, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            var tab = t;
            close.MouseLeftButtonUp += (s, e) => { e.Handled = true; CloseTab(tab); };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(title); sp.Children.Add(close);
            var b = new Border
            {
                Child = sp, Padding = new Thickness(14, 7, 12, 7), Cursor = Cursors.Hand,
                Background = CodeRender.Solid(active ? "#FF14161D" : "#FF22252E"),
                BorderBrush = CodeRender.Solid(active ? "#FF56B4E9" : "#FF3A4152"), BorderThickness = new Thickness(0, active ? 2 : 0, 1, 0),
            };
            b.MouseLeftButtonUp += (s, e) =>
            {
                if (_p.FindClass(tab.cls) is { } c && c.FindMethod(tab.method) is { } m) OpenMethod(c, m);
            };
            _tabs.Children.Add(b);
        }
    }

    // ===================== дерево проекта =====================
    void RebuildTree()
    {
        _tree.Children.Clear();
        foreach (var c in _p.Classes.ToList())
        {
            var cls = c;
            bool isEntry = cls.Name == _p.EntryClass;

            var head = new Border { Background = CodeRender.Solid("#FF2A2F3D"), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 6, 6), Margin = new Thickness(0, 8, 0, 3) };
            var row = new DockPanel();
            var more = new TextBlock { Text = "⋯", FontSize = 16, Foreground = CodeRender.Solid("#FF9AA4B8"), Cursor = Cursors.Hand, Padding = new Thickness(6, 0, 4, 0), ToolTip = "Действия с классом", VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(more, Dock.Right);
            row.Children.Add(more);
            string title = (cls.IsStatic ? "static " : "") + cls.Name + (string.IsNullOrEmpty(cls.BaseClass) ? "" : " : " + cls.BaseClass);
            row.Children.Add(new TextBlock { Text = "◼ " + title, Foreground = CodeRender.Solid("#FF4EC9B0"), FontWeight = FontWeights.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
            head.Child = row;
            more.MouseLeftButtonUp += (s, e) => ClassMenu(cls, more);
            head.MouseRightButtonUp += (s, e) => ClassMenu(cls, head);
            _tree.Children.Add(head);

            foreach (var f in cls.Fields.ToList())
            {
                var field = f;
                var item = TreeItem($"▫ {field.Name} : {field.Type}" + (string.IsNullOrWhiteSpace(field.Initial) ? "" : " = " + field.Initial), "#FF9CDCFE", false);
                item.MouseRightButtonUp += (s, e) => FieldMenu(cls, field, item);
                item.MouseLeftButtonUp += (s, e) => EditField(cls, field);
                item.ToolTip = "Щелчок — изменить поле";
                _tree.Children.Add(item);
            }

            foreach (var m in cls.Methods.ToList())
            {
                var method = m;
                bool active = _current is { } cur && cur.cls == cls.Name && cur.method == method.Name;
                string sig = method.IsCtor
                    ? $"⚙ {cls.Name}({ParamText(method)})"
                    : $"{(isEntry && method.Name == _p.EntryMethod ? "▶" : "ƒ")} {method.Name}({ParamText(method)})" + (method.IsVoid ? "" : " : " + method.ReturnType);
                var item = TreeItem(sig, "#FFDCDCAA", active);
                item.MouseLeftButtonUp += (s, e) => OpenMethod(cls, method);
                item.MouseRightButtonUp += (s, e) => MethodMenu(cls, method, item);
                item.ToolTip = "Щелчок — открыть граф метода";
                _tree.Children.Add(item);
            }

            if (cls.Fields.Count == 0 && cls.Methods.Count == 0)
                _tree.Children.Add(new TextBlock { Text = "пусто — добавьте поле или метод через «⋯»", Foreground = CodeRender.Solid("#FF7C879C"), FontSize = 11.5, Margin = new Thickness(12, 2, 0, 0) });
        }
        RefreshSidePanels();
    }

    void RefreshSidePanels() { RefreshVars(); RefreshPalette(); UpdateInspector(); }

    static string ParamText(MethodDef m) => string.Join(", ", m.Params.Select(p => $"{p.Type} {p.Name}"));

    static Border TreeItem(string text, string color, bool active) => new()
    {
        Child = new TextBlock { Text = text, Foreground = CodeRender.Solid(color), FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, FontFamily = new FontFamily("Consolas, Cascadia Mono") },
        Padding = new Thickness(14, 4, 6, 4), Cursor = Cursors.Hand, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 1, 0, 1),
        Background = CodeRender.Solid(active ? "#FF2C4A63" : "#00000000"),
    };

    ContextMenu Menu(params (string title, Action act, bool enabled)[] items)
    {
        var m = new ContextMenu { Resources = Resources };
        foreach (var (t, a, en) in items)
        {
            var mi = new MenuItem { Header = t, IsEnabled = en };
            mi.Click += (s, e) => a();
            m.Items.Add(mi);
        }
        return m;
    }

    void ClassMenu(ClassDef c, UIElement anchor)
    {
        bool entry = c.Name == _p.EntryClass;
        var menu = Menu(
            ("Добавить поле…", () => AddField(c), !c.IsStatic || true),
            ("Добавить метод…", () => AddMethod(c), true),
            ("Добавить конструктор…", () => AddCtor(c), !c.IsStatic && c.Constructor is null),
            ("Изменить класс…", () => EditClass(c), !entry),
            ("Удалить класс", () => DeleteClass(c), !entry));
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    void FieldMenu(ClassDef c, FieldDef f, UIElement anchor)
    {
        var menu = Menu(("Изменить поле…", () => EditField(c, f), true), ("Удалить поле", () => DeleteField(c, f), true));
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    void MethodMenu(ClassDef c, MethodDef m, UIElement anchor)
    {
        bool entry = c.Name == _p.EntryClass && m.Name == _p.EntryMethod;
        var menu = Menu(
            ("Открыть граф", () => OpenMethod(c, m), true),
            ("Изменить сигнатуру…", () => EditMethod(c, m), !entry),
            ("Удалить метод", () => DeleteMethod(c, m), !entry));
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    // ===================== диалоги: классы, поля, методы =====================
    Window? Owner => Window.GetWindow(this);

    IEnumerable<string> TypeOptions(bool withVoid = false)
    {
        var l = new List<string>();
        if (withVoid) l.Add("void");
        l.AddRange(GraphTypes.Primitive);
        l.Add("object");
        l.AddRange(GraphTypes.Primitive.Select(t => $"List<{t}>"));
        foreach (var c in _p.Classes.Where(c => !c.IsStatic)) { l.Add(c.Name); l.Add($"List<{c.Name}>"); }
        return l;
    }

    static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue", "decimal", "default",
        "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto",
        "if", "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc",
        "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
    };

    static string? CheckName(string name, string what)
    {
        if (string.IsNullOrWhiteSpace(name)) return $"Введите {what}.";
        if (!(char.IsLetter(name[0]) || name[0] == '_') || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
            return $"{what}: только буквы, цифры и «_», не начинать с цифры.";
        if (Keywords.Contains(name)) return $"«{name}» — зарезервированное слово C#.";
        return null;
    }

    static (List<ParamDef>? list, string? error) ParseParams(string text)
    {
        var list = new List<ParamDef>();
        foreach (var part in text.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = part.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return (null, $"Параметр «{part}»: нужно «тип имя», например int count.");
            string name = tokens[^1], type = string.Join(" ", tokens[..^1]);
            var err = CheckName(name, "имя параметра");
            if (err is not null) return (null, err);
            if (list.Any(p => p.Name == name)) return (null, $"Параметр «{name}» указан дважды.");
            list.Add(new ParamDef { Name = name, Type = type });
        }
        return (list, null);
    }

    void AddClass()
    {
        var r = FormDialog.Show(Owner, "Новый класс", new[]
        {
            new FormField("name", "Имя класса", FormKind.Text, "Player", null, "Например: Player, Account, Car — с заглавной буквы."),
            new FormField("base", "Наследует от", FormKind.Combo, "(нет)", new[] { "(нет)" }.Concat(_p.Classes.Where(c => !c.IsStatic).Select(c => c.Name))),
            new FormField("static", "Статический", FormKind.Check, "false", null, "static — класс-набор функций, объекты создавать нельзя"),
        }, v => CheckName(v["name"], "имя класса") ?? (_p.FindClass(v["name"]) is not null ? "Такой класс уже есть." : null));
        if (r is null) return;

        var c = new ClassDef { Name = r["name"], BaseClass = r["base"] == "(нет)" ? null : r["base"], IsStatic = r["static"] == "true" };
        _p.Classes.Add(c);
        AfterStructureChange();
    }

    void EditClass(ClassDef c)
    {
        var r = FormDialog.Show(Owner, "Класс " + c.Name, new[]
        {
            new FormField("name", "Имя класса", FormKind.Text, c.Name),
            new FormField("base", "Наследует от", FormKind.Combo, c.BaseClass ?? "(нет)", new[] { "(нет)" }.Concat(_p.Classes.Where(x => !x.IsStatic && x != c).Select(x => x.Name))),
            new FormField("static", "Статический", FormKind.Check, c.IsStatic ? "true" : "false", null, "static"),
        }, v => CheckName(v["name"], "имя класса") ?? (v["name"] != c.Name && _p.FindClass(v["name"]) is not null ? "Такой класс уже есть." : null));
        if (r is null) return;

        string old = c.Name;
        c.BaseClass = r["base"] == "(нет)" ? null : r["base"];
        c.IsStatic = r["static"] == "true";
        if (r["name"] != old) RenameClass(old, r["name"]);
        AfterStructureChange();
    }

    void DeleteClass(ClassDef c)
    {
        if (MessageBox.Show(Owner, $"Удалить класс «{c.Name}» вместе с его методами?\nНоды, которые его вызывали, тоже исчезнут.", "Удаление класса",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RemoveNodes(n => n.Prop("Class") == c.Name);
        _p.Classes.Remove(c);
        foreach (var x in _p.Classes.Where(x => x.BaseClass == c.Name)) x.BaseClass = null;
        _open.RemoveAll(t => t.cls == c.Name);
        AfterStructureChange();
    }

    void AddField(ClassDef c)
    {
        var r = FormDialog.Show(Owner, $"Новое поле в {c.Name}", FieldForm(c, null), v => FieldCheck(c, null, v));
        if (r is null) return;
        c.Fields.Add(new FieldDef { Name = r["name"], Type = r["type"], Initial = r["init"], IsStatic = r["static"] == "true", IsProperty = r["prop"] == "true" });
        AfterStructureChange();
    }

    void EditField(ClassDef c, FieldDef f)
    {
        var r = FormDialog.Show(Owner, $"Поле {c.Name}.{f.Name}", FieldForm(c, f), v => FieldCheck(c, f, v));
        if (r is null) return;
        string old = f.Name;
        f.Type = r["type"]; f.Initial = r["init"]; f.IsStatic = r["static"] == "true"; f.IsProperty = r["prop"] == "true";
        if (r["name"] != old) { f.Name = r["name"]; RenameField(c.Name, old, f.Name); }
        AfterStructureChange();
    }

    IEnumerable<FormField> FieldForm(ClassDef c, FieldDef? f) => new[]
    {
        new FormField("name", "Имя поля", FormKind.Text, f?.Name ?? "", null, "Например: Name, Hp, Balance"),
        new FormField("type", "Тип", FormKind.EditableCombo, f?.Type ?? "int", TypeOptions()),
        new FormField("init", "Начальное значение (выражение C#, можно пусто)", FormKind.Text, f?.Initial ?? "", null, "100   или   \"Привет\"   или   new List<int>()"),
        new FormField("static", "Статическое", FormKind.Check, f?.IsStatic == true ? "true" : "false", null, "static — одно значение на весь класс"),
        new FormField("prop", "Свойство", FormKind.Check, f?.IsProperty == true ? "true" : "false", null, "{ get; set; } вместо обычного поля"),
    };

    string? FieldCheck(ClassDef c, FieldDef? self, Dictionary<string, string> v)
    {
        var e = CheckName(v["name"], "имя поля");
        if (e is not null) return e;
        if (c.Fields.Any(x => x != self && x.Name == v["name"])) return "Такое поле уже есть.";
        return string.IsNullOrWhiteSpace(v["type"]) ? "Укажите тип." : null;
    }

    void DeleteField(ClassDef c, FieldDef f)
    {
        RemoveNodes(n => n.Kind is "GetField" or "SetField" && n.Prop("Class") == c.Name && n.Prop("Field") == f.Name);
        c.Fields.Remove(f);
        AfterStructureChange();
    }

    void AddMethod(ClassDef c)
    {
        var r = FormDialog.Show(Owner, $"Новый метод в {c.Name}", MethodForm(c, null), v => MethodCheck(c, null, v));
        if (r is null) return;
        var m = new MethodDef { Name = r["name"], ReturnType = r["ret"], IsStatic = r["static"] == "true" || c.IsStatic, Modifier = r["mod"] };
        m.Params.AddRange(ParseParams(r["params"]).list!);
        m.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 40, Y = 80 });
        c.Methods.Add(m);
        AfterStructureChange();
        OpenMethod(c, m);
    }

    void AddCtor(ClassDef c)
    {
        var r = FormDialog.Show(Owner, $"Конструктор {c.Name}", new[]
        {
            new FormField("params", "Параметры (тип имя, через запятую)", FormKind.Multi, "string name", null, "Конструктор вызывается при создании объекта: new " + c.Name + "(...)"),
        }, v => ParseParams(v["params"]).error);
        if (r is null) return;
        var m = new MethodDef { Name = MethodDef.CtorName, ReturnType = "void" };
        m.Params.AddRange(ParseParams(r["params"]).list!);
        m.Graph.Nodes.Add(new NodeData { Id = "entry", Kind = "Entry", X = 40, Y = 80 });
        c.Methods.Insert(0, m);
        AfterStructureChange();
        OpenMethod(c, m);
    }

    void EditMethod(ClassDef c, MethodDef m)
    {
        if (m.IsCtor)
        {
            var rc = FormDialog.Show(Owner, "Конструктор " + c.Name, new[] { new FormField("params", "Параметры", FormKind.Multi, ParamText(m)) }, v => ParseParams(v["params"]).error);
            if (rc is null) return;
            ApplyParams(c, m, ParseParams(rc["params"]).list!);
            AfterStructureChange();
            return;
        }

        var r = FormDialog.Show(Owner, $"Метод {c.Name}.{m.Name}", MethodForm(c, m), v => MethodCheck(c, m, v));
        if (r is null) return;
        string old = m.Name;
        m.ReturnType = r["ret"]; m.IsStatic = r["static"] == "true" || c.IsStatic; m.Modifier = r["mod"];
        ApplyParams(c, m, ParseParams(r["params"]).list!);
        if (r["name"] != old) { m.Name = r["name"]; RenameMethod(c.Name, old, m.Name); for (int i = 0; i < _open.Count; i++) if (_open[i] == (c.Name, old)) _open[i] = (c.Name, m.Name); if (_current == (c.Name, old)) _current = (c.Name, m.Name); }
        AfterStructureChange();
    }

    IEnumerable<FormField> MethodForm(ClassDef c, MethodDef? m) => new[]
    {
        new FormField("name", "Имя метода", FormKind.Text, m?.Name ?? "", null, "Глагол с заглавной буквы: Attack, GetTotal, PrintReport"),
        new FormField("ret", "Что возвращает", FormKind.EditableCombo, m?.ReturnType ?? "void", TypeOptions(true), "void — ничего не возвращает"),
        new FormField("params", "Параметры (тип имя, через запятую)", FormKind.Multi, m is null ? "" : ParamText(m), null, "int damage, string name"),
        new FormField("static", "Статический", FormKind.Check, (m?.IsStatic ?? false) || c.IsStatic ? "true" : "false", null, "static — вызывается без объекта: Class.Method()"),
        new FormField("mod", "Модификатор", FormKind.Combo, m?.Modifier ?? "", new[] { "", "virtual", "override" }, "virtual — потомки могут переопределить; override — переопределяет метод предка"),
    };

    string? MethodCheck(ClassDef c, MethodDef? self, Dictionary<string, string> v)
    {
        var e = CheckName(v["name"], "имя метода");
        if (e is not null) return e;
        if (c.Methods.Any(x => x != self && x.Name == v["name"])) return "Метод с таким именем уже есть (перегрузки пока не поддерживаются).";
        if (string.IsNullOrWhiteSpace(v["ret"])) return "Укажите, что возвращает метод (void — ничего).";
        return ParseParams(v["params"]).error;
    }

    void ApplyParams(ClassDef c, MethodDef m, List<ParamDef> neu)
    {
        // тот же состав параметров, но другие имена — это переименование: связи сохраняем
        if (neu.Count == m.Params.Count)
            for (int i = 0; i < neu.Count; i++)
                if (neu[i].Name != m.Params[i].Name) RenameParam(c.Name, m, m.Params[i].Name, neu[i].Name);
        m.Params = neu;
    }

    void DeleteMethod(ClassDef c, MethodDef m)
    {
        RemoveNodes(n => n.Kind == "CallMethod" && n.Prop("Class") == c.Name && n.Prop("Method") == m.Name);
        c.Methods.Remove(m);
        _open.RemoveAll(t => t == (c.Name, m.Name));
        if (_current == (c.Name, m.Name)) { _current = null; _canvas.Unbind(); }
        AfterStructureChange();
    }

    // ---------- переименования и удаления с обновлением нод ----------
    IEnumerable<(ClassDef c, MethodDef m, NodeData n)> AllNodes() =>
        _p.Classes.SelectMany(c => c.Methods.SelectMany(m => m.Graph.Nodes.Select(n => (c, m, n))));

    static string ReplaceType(string t, string old, string neu) => t == old ? neu : t == $"List<{old}>" ? $"List<{neu}>" : t;

    void RenameClass(string old, string neu)
    {
        foreach (var c in _p.Classes)
        {
            if (c.BaseClass == old) c.BaseClass = neu;
            foreach (var f in c.Fields) f.Type = ReplaceType(f.Type, old, neu);
            foreach (var m in c.Methods)
            {
                m.ReturnType = ReplaceType(m.ReturnType, old, neu);
                foreach (var p in m.Params) p.Type = ReplaceType(p.Type, old, neu);
            }
        }
        foreach (var (_, _, n) in AllNodes())
        {
            if (n.Props.TryGetValue("Class", out var cn) && cn == old) n.Props["Class"] = neu;
            foreach (var key in new[] { "Type", "T", "To" })
                if (n.Props.TryGetValue(key, out var t)) n.Props[key] = ReplaceType(t, old, neu);
        }
        _p.Classes.First(c => c.Name == old).Name = neu;
        if (_p.EntryClass == old) _p.EntryClass = neu;
        for (int i = 0; i < _open.Count; i++) if (_open[i].cls == old) _open[i] = (neu, _open[i].method);
        if (_current is { } cur && cur.cls == old) _current = (neu, cur.method);
    }

    void RenameMethod(string cls, string old, string neu)
    {
        foreach (var (_, _, n) in AllNodes())
            if (n.Kind == "CallMethod" && n.Prop("Class") == cls && n.Prop("Method") == old) n.Props["Method"] = neu;
    }

    void RenameField(string cls, string old, string neu)
    {
        foreach (var (c, _, n) in AllNodes())
        {
            if (n.Kind is "GetField" or "SetField" && n.Prop("Class") == cls && n.Prop("Field") == old) n.Props["Field"] = neu;
            if (n.Kind is "GetVar" or "SetVar" && c.Name == cls && n.Prop("Name") == old) n.Props["Name"] = neu;
        }
    }

    void RenameParam(string cls, MethodDef m, string old, string neu)
    {
        foreach (var w in m.Graph.Wires.Where(w => w.FromPin == "p:" + old && m.Graph.Find(w.FromNode)?.Kind == "Entry")) w.FromPin = "p:" + neu;
        foreach (var n in m.Graph.Nodes.Where(n => n.Kind is "GetVar" or "SetVar" && n.Prop("Name") == old)) n.Props["Name"] = neu;

        foreach (var (_, caller, n) in AllNodes())
        {
            bool target = n.Kind == "CallMethod" && n.Prop("Class") == cls && n.Prop("Method") == m.Name
                          || n.Kind == "NewObject" && m.IsCtor && n.Prop("Class") == cls;
            if (!target) continue;
            foreach (var w in caller.Graph.Wires.Where(w => w.ToNode == n.Id && w.ToPin == "p:" + old)) w.ToPin = "p:" + neu;
            if (n.Props.Remove("in:p:" + old, out var val)) n.Props["in:p:" + neu] = val;
        }
    }

    void RemoveNodes(Func<NodeData, bool> pred)
    {
        foreach (var c in _p.Classes)
            foreach (var m in c.Methods)
            {
                var doomed = m.Graph.Nodes.Where(n => n.Kind != "Entry" && pred(n)).Select(n => n.Id).ToHashSet();
                if (doomed.Count == 0) continue;
                m.Graph.Nodes.RemoveAll(n => doomed.Contains(n.Id));
                m.Graph.Wires.RemoveAll(w => doomed.Contains(w.FromNode) || doomed.Contains(w.ToNode));
            }
    }

    /// <summary>После любой правки структуры проекта: подчистить провода, обновить холст, код, историю.</summary>
    void AfterStructureChange()
    {
        GraphScope.PruneAll(_p);
        _open.RemoveAll(t => _p.FindClass(t.cls)?.FindMethod(t.method) is null);

        RebuildTree();
        if (_current is { } cur && _p.FindClass(cur.cls)?.FindMethod(cur.method) is { } m) _canvas.Bind(_p, _p.FindClass(cur.cls)!, m);
        else if (_open.Count > 0 && _p.FindClass(_open[^1].cls) is { } oc && oc.FindMethod(_open[^1].method) is { } om) { OpenMethod(oc, om); return; }
        else { _current = null; _canvas.Unbind(); _emptyHint.Visibility = Visibility.Visible; }

        RefreshTabs();
        RegenerateCode();
        PushHistory();
        ScheduleSave();
    }

    // ===================== переменные метода, палитра, свойства =====================
    (ClassDef c, MethodDef m)? CurrentMethod() =>
        _current is { } cur && _p.FindClass(cur.cls) is { } c && c.FindMethod(cur.method) is { } m ? (c, m) : null;

    string _paletteSig = "";

    void RefreshPalette()
    {
        if (CurrentMethod() is not { } cm) { _palettePanel.SetItems(new()); _paletteSig = ""; return; }
        var items = NodeSpecs.Palette(_p, cm.c, cm.m);
        string sig = cm.c.Name + "." + cm.m.Name + ":" + items.Count + ":" + string.Join("|", items.Where(i => i.Kind is "GetVar" or "SetVar" or "CallMethod" or "NewObject" or "GetField" or "SetField").Select(i => i.Title));
        if (sig == _paletteSig) return;
        _paletteSig = sig;
        _palettePanel.SetItems(items);
    }

    void RefreshVars()
    {
        _vars.Children.Clear();
        if (CurrentMethod() is not { } cm)
        {
            _varsTitle.Text = "Переменные";
            _vars.Children.Add(Note("Откройте метод — здесь появятся его переменные."));
            return;
        }

        var (c, m) = cm;
        _varsTitle.Text = "Переменные";
        _varsTitle.ToolTip = "Переменные метода " + (m.IsCtor ? "(конструктор)" : m.Name);

        foreach (var prm in m.Params)
        {
            var name = prm.Name;
            _vars.Children.Add(VarRow($"{prm.Name} : {prm.Type}", "параметр метода", "#FF9AA4B8",
                () => AddVarNode("GetVar", name), null, null));
        }
        foreach (var loc in m.Locals.ToList())
        {
            var local = loc;
            string text = $"{local.Name} : {local.Type}" + (string.IsNullOrWhiteSpace(local.Initial) ? "" : " = " + local.Initial);
            _vars.Children.Add(VarRow(text, "щёлкните — изменить, правый клик — удалить", "#FF9CDCFE",
                () => AddVarNode("GetVar", local.Name), () => AddVarNode("SetVar", local.Name), () => EditLocal(m, local), () => DeleteLocal(m, local)));
        }
        if (m.Params.Count == 0 && m.Locals.Count == 0)
            _vars.Children.Add(Note("Переменная — коробка для значения. Нажмите «+ Переменная»: задайте имя, тип и начальное значение, потом берите её и меняйте нодами."));
    }

    static TextBlock Note(string text) => new() { Text = text, Foreground = CodeRender.Solid("#FF8C97AD"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 4, 2) };

    UIElement VarRow(string text, string tip, string color, Action get, Action? set, Action? edit, Action? del = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new Border
        {
            Child = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12.5, Foreground = CodeRender.Solid(color), TextTrimming = TextTrimming.CharacterEllipsis },
            Padding = new Thickness(7, 4, 4, 4), CornerRadius = new CornerRadius(4), ToolTip = tip, Cursor = edit is null ? Cursors.Arrow : Cursors.Hand,
        };
        if (edit is not null)
        {
            label.MouseEnter += (s, e) => label.Background = CodeRender.Solid("#FF2A3550");
            label.MouseLeave += (s, e) => label.Background = Brushes.Transparent;
            label.MouseLeftButtonUp += (s, e) => edit();
            if (del is not null)
                label.MouseRightButtonUp += (s, e) =>
                {
                    var menu = Menu(("Изменить…", edit, true), ("Удалить переменную", del, true));
                    menu.PlacementTarget = label;
                    menu.IsOpen = true;
                };
        }
        grid.Children.Add(label);

        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(MiniBtn("Взять", "Добавить на граф ноду «значение переменной»", get));
        if (set is not null) btns.Children.Add(MiniBtn("Задать", "Добавить на граф ноду «присвоить значение»", set));
        Grid.SetColumn(btns, 1);
        grid.Children.Add(btns);
        return grid;
    }

    static Border MiniBtn(string text, string tip, Action act)
    {
        var b = new Border
        {
            Child = new TextBlock { Text = text, FontSize = 11.5, Foreground = CodeRender.Solid("#FFC5CCDA") },
            Padding = new Thickness(7, 3, 7, 3), Margin = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
            Background = CodeRender.Solid("#FF2E3341"), BorderBrush = CodeRender.Solid("#FF3A4152"), BorderThickness = new Thickness(1), ToolTip = tip,
        };
        b.MouseEnter += (s, e) => b.Background = CodeRender.Solid("#FF3A4152");
        b.MouseLeave += (s, e) => b.Background = CodeRender.Solid("#FF2E3341");
        b.MouseLeftButtonUp += (s, e) => act();
        return b;
    }

    void AddVarNode(string kind, string name)
    {
        if (_current is null) return;
        _canvas.AddFromPalette(new PaletteItem(name, "Переменные", kind, new() { ["Name"] = name }));
    }

    void AddLocal()
    {
        if (CurrentMethod() is not { } cm) { SetStatus("Сначала откройте метод: щёлкните по нему слева."); return; }
        var m = cm.m;
        var r = FormDialog.Show(Owner, "Новая переменная в " + (m.IsCtor ? "конструкторе" : m.Name), LocalForm(null), v => LocalCheck(m, null, v), "Создать");
        if (r is null) return;
        var loc = new LocalDef { Name = r["name"], Type = r["type"], Initial = r["init"] };
        m.Locals.Add(loc);
        AfterStructureChange();
        // сразу кладём на граф ноду «значение» — чтобы переменная была видна
        AddVarNode("GetVar", loc.Name);
    }

    void EditLocal(MethodDef m, LocalDef loc)
    {
        var r = FormDialog.Show(Owner, "Переменная " + loc.Name, LocalForm(loc), v => LocalCheck(m, loc, v), "Сохранить");
        if (r is null) return;
        string old = loc.Name;
        loc.Type = r["type"]; loc.Initial = r["init"];
        if (r["name"] != old)
        {
            loc.Name = r["name"];
            foreach (var n in m.Graph.Nodes.Where(n => n.Kind is "GetVar" or "SetVar" && n.Prop("Name") == old)) n.Props["Name"] = loc.Name;
        }
        AfterStructureChange();
    }

    void DeleteLocal(MethodDef m, LocalDef loc)
    {
        var doomed = m.Graph.Nodes.Where(n => n.Kind is "GetVar" or "SetVar" && n.Prop("Name") == loc.Name).Select(n => n.Id).ToHashSet();
        m.Graph.Nodes.RemoveAll(n => doomed.Contains(n.Id));
        m.Graph.Wires.RemoveAll(w => doomed.Contains(w.FromNode) || doomed.Contains(w.ToNode));
        m.Locals.Remove(loc);
        AfterStructureChange();
    }

    IEnumerable<FormField> LocalForm(LocalDef? l) => new[]
    {
        new FormField("name", "Имя переменной", FormKind.Text, l?.Name ?? "", null, "Латиницей, с маленькой буквы: score, name, total, count"),
        new FormField("type", "Тип", FormKind.EditableCombo, l?.Type ?? "int", TypeOptions(), "int — целое, double — дробное, string — текст, bool — да/нет, decimal — деньги"),
        new FormField("init", "Начальное значение", FormKind.Text, l?.Initial ?? "", null, "Можно пусто: для чисел будет 0, для текста \"\", для списка — пустой список"),
        };

    string? LocalCheck(MethodDef m, LocalDef? self, Dictionary<string, string> v)
    {
        var e = CheckName(v["name"], "имя переменной");
        if (e is not null) return e;
        if (m.Params.Any(p => p.Name == v["name"]) || m.Locals.Any(x => x != self && x.Name == v["name"]))
            return "Переменная или параметр с таким именем в этом методе уже есть.";
        return string.IsNullOrWhiteSpace(v["type"]) ? "Укажите тип." : null;
    }

    void UpdateInspector()
    {
        if (CurrentMethod() is not { } cm) { _inspector.ShowNothing(); return; }
        var node = _canvas.FindNode(_selectedNode);
        if (node is null)
        {
            var (c, m) = cm;
            string sig = m.IsCtor ? $"конструктор {c.Name}({ParamText(m)})" : $"метод {c.Name}.{m.Name}({ParamText(m)})" + (m.IsVoid ? "" : $" → {m.ReturnType}");
            _inspector.ShowNothing(sig + "\n\nВыберите ноду на холсте — здесь появятся её описание и настройки.");
            return;
        }
        _inspector.Show(node, _p, cm.c, cm.m, (n, structural) =>
        {
            _canvas.NodeEdited(n.Id, structural);
            if (structural) Dispatcher.BeginInvoke(UpdateInspector, DispatcherPriority.Background);
        });
    }

    // ===================== код, подсветка, проблемы =====================
    void ScheduleCode() { _codeTimer.Stop(); _codeTimer.Start(); }

    void RegenerateCode()
    {
        _lastGen = GraphCodeGen.Generate(_p, withUsings: true);
        RefreshPalette();
        HighlightSelected();

        _issues.Items.Clear();
        var list = _lastGen.Issues;
        _issuesTitle.Text = list.Count == 0 ? "Проблемы: нет ✓" : $"Проблемы: {list.Count(i => i.IsError)} ошибок, {list.Count(i => !i.IsError)} предупреждений";
        foreach (var i in list)
        {
            var tb = new TextBlock
            {
                Text = (i.IsError ? "✖ " : "⚠ ") + (i.Class.Length > 0 ? $"{i.Class}.{i.Method}: " : "") + i.Message,
                Foreground = CodeRender.Solid(i.IsError ? "#FFE9573F" : "#FFE8B04C"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Tag = i, Cursor = Cursors.Hand,
            };
            _issues.Items.Add(tb);
        }

        if (_current is { } cur)
            _canvas.MarkErrors(list.Where(i => i.IsError && i.Class == cur.cls && i.Method == cur.method && i.NodeId.Length > 0).Select(i => i.NodeId));
    }

    void HighlightSelected()
    {
        if (_lastGen is null) return;
        int from = 0, to = 0;
        if (_selectedNode is not null && _current is { } cur &&
            _lastGen.NodeLines.TryGetValue($"{cur.cls}.{cur.method}:{_selectedNode}", out var range))
            (from, to) = range;

        CodeRender.Fill(_code, _lastGen.Code, from, to);
        if (from > 0)
            _codeScroll.ScrollToVerticalOffset(Math.Max(0, (from - 4) * 19.0));
    }

    void GoToIssue(GraphIssue issue)
    {
        if (_p.FindClass(issue.Class) is not { } c || c.FindMethod(issue.Method) is not { } m) return;
        OpenMethod(c, m);
        if (issue.NodeId.Length > 0) Dispatcher.BeginInvoke(() => _canvas.SelectNode(issue.NodeId, center: true), DispatcherPriority.Background);
    }

    void SetStatus(string text) => _status.Text = text;

    // ===================== запуск =====================
    async Task RunAsync()
    {
        _runBtn.IsEnabled = false;
        _out.Foreground = CodeRender.Solid("#FF9AA4B8");
        _out.Text = "Собираю код и запускаю…";
        try
        {
            var gen = GraphCodeGen.Generate(_p, withUsings: false);
            if (gen.HasErrors)
            {
                _out.Foreground = CodeRender.Solid("#FFE9573F");
                _out.Text = "Сначала исправьте ошибки в списке «Проблемы» слева внизу:\n" +
                            string.Join("\n", gen.Issues.Where(i => i.IsError).Select(i => $"• {i.Class}.{i.Method}: {i.Message}"));
                return;
            }

            string work = Path.Combine(Path.GetTempPath(), "CSharpCoachGraph", Guid.NewGuid().ToString("N"));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (compile, run) = await Task.Run(() =>
            {
                var comp = IlWorkbench.Compile(gen.Code, work, "graph");
                return (comp, comp.Success ? SandboxRunner.Run(comp.AssemblyPath!, 8000, work) : null);
            });

            if (!compile.Success)
            {
                _out.Foreground = CodeRender.Solid("#FFE9573F");
                _out.Text = "Компилятор C# не принял код (так же ответит и Visual Studio):\n" + compile.Diagnostics +
                            "\n\nЧаще всего причина — переменная создана внутри ветки if/цикла и используется за её пределами.";
                return;
            }

            string text = run!.Output.Trim();
            _out.Foreground = CodeRender.Solid(run.Success ? "#FF63C77A" : "#FFE9573F");
            _out.Text = (text.Length == 0 ? "(программа ничего не вывела)" : text) +
                        (run.Error.Trim().Length > 0 ? "\n\n" + run.Error.Trim() : "") +
                        (run.TimedOut ? "\n\nПрограмма не завершилась — похоже на бесконечный цикл." : "");
            SetStatus($"выполнено за {sw.ElapsedMilliseconds} мс");
            try { Directory.Delete(work, true); } catch { /* временная папка — не критично */ }
        }
        catch (Exception ex)
        {
            _out.Foreground = CodeRender.Solid("#FFE9573F");
            _out.Text = "Сбой запуска: " + ex.Message;
        }
        finally
        {
            _runBtn.IsEnabled = true;
        }
    }

    // ===================== файлы и примеры =====================
    void ShowSamples(UIElement anchor)
    {
        var menu = new ContextMenu { Resources = Resources };
        foreach (var s in GraphSamples.All)
        {
            var sample = s;
            var mi = new MenuItem { Header = $"{sample.Title}  —  {sample.Description}" };
            mi.Click += (_, _) =>
            {
                if (MessageBox.Show(Owner, "Текущий проект будет заменён примером. Продолжить?\n(Сохранить его можно кнопкой «Сохранить как…»)", "Пример", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                SetProject(Arranged(sample.Build()), null, resetHistory: false);
                PushHistory();
                ScheduleSave();
            };
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = anchor;
        menu.IsOpen = true;
    }

    void NewProject()
    {
        if (MessageBox.Show(Owner, "Начать новый пустой проект? Текущий будет заменён.", "Новый проект", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetProject(GraphProject.CreateEmpty(), null, resetHistory: false);
        PushHistory();
        ScheduleSave();
    }

    void OpenFile()
    {
        var dlg = new OpenFileDialog { Filter = "Граф C# (*.csgraph)|*.csgraph|Все файлы|*.*", Title = "Открыть проект" };
        if (dlg.ShowDialog(Owner) != true) return;
        try
        {
            SetProject(GraphJson.Load(File.ReadAllText(dlg.FileName)), dlg.FileName, resetHistory: false);
            PushHistory();
            SetStatus("открыт " + Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Owner, "Не удалось открыть файл: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void SaveFile(bool forceDialog)
    {
        if (forceDialog || _filePath is null)
        {
            var dlg = new SaveFileDialog { Filter = "Граф C# (*.csgraph)|*.csgraph", FileName = _p.Name, Title = "Сохранить проект" };
            if (dlg.ShowDialog(Owner) != true) return;
            _filePath = dlg.FileName;
        }
        try
        {
            File.WriteAllText(_filePath!, GraphJson.Save(_p));
            SetStatus("сохранено: " + Path.GetFileName(_filePath));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Owner, "Не удалось сохранить: " + ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void ScheduleSave() { if (_restoring) return; _saveTimer.Stop(); _saveTimer.Start(); }

    void Autosave()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AutosavePath)!);
            File.WriteAllText(AutosavePath, GraphJson.Save(_p));
        }
        catch { /* автосохранение — приятное дополнение */ }
    }
}
