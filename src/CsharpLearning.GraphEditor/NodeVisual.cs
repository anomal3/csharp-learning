using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Ссылка на конкретный пин конкретной ноды.</summary>
public sealed record PinRef(string NodeId, string PinId, bool IsOutput, bool IsExec, string Type);

/// <summary>Всё, что нужно ноде, чтобы нарисоваться и сообщать об изменениях.</summary>
public sealed class NodeVisualContext
{
    public GraphProject P { get; init; } = null!;
    public ClassDef Cls { get; init; } = null!;
    public MethodDef M { get; init; } = null!;
    public HashSet<string> ConnectedInputs { get; init; } = new();
    /// <summary>(нода, имя свойства, нужна ли перерисовка ноды: могли измениться пины).</summary>
    public Action<NodeVisual, string, bool> PropChanged { get; init; } = (_, _, _) => { };
    public Action<NodeVisual> PinValueChanged { get; init; } = _ => { };
}

/// <summary>
/// Одна нода на холсте. Ширина считается по содержимому, высоты строк фиксированы — поэтому
/// координаты пинов вычисляются формулой, а не снимаются с готового дерева элементов.
/// </summary>
public sealed class NodeVisual : Canvas
{
    public const double HeaderH = 38, RowH = 30, InlineH = 34, Bottom = 12, MinW = 210;
    const double LabelSize = 13;

    public NodeData Data { get; }
    public NodeSpec Spec { get; }
    public List<Pin> Pins { get; }
    public double NodeHeight { get; private set; }
    public double NodeWidth { get; private set; }

    /// <summary>Ключ: «i:id» для входа, «o:id» для выхода.</summary>
    public Dictionary<string, Point> Anchors { get; } = new();
    public Dictionary<string, FrameworkElement> Dots { get; } = new();

    readonly Border _frame = new();
    readonly System.Windows.Media.Effects.DropShadowEffect _shadow = new() { BlurRadius = 16, Opacity = 0.5, ShadowDepth = 3, Color = Colors.Black };

    public event Action<NodeVisual, Pin, MouseButtonEventArgs>? PinMouseDown;

    public static string Key(Pin p) => (p.IsOutput ? "o:" : "i:") + p.Id;
    public static string Key(string id, bool output) => (output ? "o:" : "i:") + id;

    static double TextWidth(string text, double size, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brushes.Black, 1.25);
        return Math.Ceiling(ft.Width);
    }

    static double EditorWidth(string type) => type switch
    {
        "int" or "long" or "decimal" => 78,
        "double" => 84,
        "char" => 52,
        "bool" => 26,
        _ => 128,
    };

    public NodeVisual(NodeData data, NodeSpec spec, List<Pin> pins, NodeVisualContext ctx)
    {
        Data = data; Spec = spec; Pins = pins;
        var nctx = new NodeCtx(data, ctx.P, ctx.Cls, ctx.M);

        var inputs = pins.Where(p => !p.IsOutput).OrderByDescending(p => p.IsExec).ToList();
        var outputs = pins.Where(p => p.IsOutput).OrderByDescending(p => p.IsExec).ToList();
        int rows = Math.Max(inputs.Count, outputs.Count);
        var inline = spec.Props.Where(p => p.Inline).ToList();

        // «Значение переменной», this и подобные — просто кружок с названием: компактная плашка без пустого тела
        bool compact = spec.Mode == NodeMode.Pure && inputs.Count == 0 && outputs.Count == 1 && inline.Count == 0;
        if (compact) rows = 0;

        string titleText = spec.Title(nctx);

        // ---------- ширина по содержимому ----------
        double w = Math.Max(compact ? 150 : MinW, TextWidth(titleText, 13.5, true) + (compact ? 90 : 64));
        foreach (var pd in inline)
            w = Math.Max(w, 28 + TextWidth(pd.Label, 12) + 10 + InlineWidth(pd));

        double LeftNeed(Pin p)
        {
            double need = 22 + TextWidth(p.Label, LabelSize);
            if (!p.IsExec && !ctx.ConnectedInputs.Contains(p.Id) && HasEditor(p)) need += 10 + EditorWidth(p.Type);
            return need;
        }
        double RightNeed(Pin p) => 22 + TextWidth(p.Label, LabelSize);

        for (int i = 0; i < rows; i++)
        {
            double l = i < inputs.Count ? LeftNeed(inputs[i]) : 0;
            double r = i < outputs.Count ? RightNeed(outputs[i]) : 0;
            w = Math.Max(w, l + r + 26);
        }
        w = Math.Ceiling(w / 10) * 10;
        NodeWidth = w;

        double inlineTop = HeaderH + 8;
        double pinsTop = inlineTop + inline.Count * InlineH + (inline.Count > 0 ? 4 : 2);
        NodeHeight = compact ? HeaderH + 3 : pinsTop + rows * RowH + Bottom;
        Width = w; Height = NodeHeight;
        Focusable = false;

        // ---------- корпус ----------
        _frame.Width = w; _frame.Height = NodeHeight;
        _frame.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#FF2B3040"), (Color)ColorConverter.ConvertFromString("#FF232733"), 90);
        _frame.BorderBrush = CodeRender.Solid("#FF4A5266");
        _frame.BorderThickness = new Thickness(1.5);
        _frame.CornerRadius = new CornerRadius(10);
        _frame.Effect = _shadow;
        Children.Add(_frame);

        // ---------- шапка ----------
        var baseColor = (Color)ColorConverter.ConvertFromString(spec.Color);
        var header = new Border
        {
            Width = w - 3, Height = HeaderH - 1.5,
            Background = new LinearGradientBrush(baseColor, Darken(baseColor, 0.82), 0),
            CornerRadius = compact ? new CornerRadius(8.5) : new CornerRadius(8.5, 8.5, 0, 0),
        };
        SetLeft(header, 1.5); SetTop(header, 1.5);
        Children.Add(header);

        var badge = new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), IsHitTestVisible = false,
            Child = new TextBlock { Text = spec.Icon, FontSize = 12.5, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        SetLeft(badge, 9); SetTop(badge, 7);
        Children.Add(badge);

        var title = new TextBlock
        {
            Text = titleText, Foreground = CodeRender.Solid("#FF10131A"), FontWeight = FontWeights.SemiBold, FontSize = 13.5,
            IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis, Width = w - 50,
        };
        SetLeft(title, 40); SetTop(title, 9);
        Children.Add(title);

        // ---------- короткие поля прямо в ноде ----------
        double y = inlineTop;
        foreach (var pd in inline)
        {
            var label = new TextBlock { Text = pd.Label, Foreground = CodeRender.Solid("#FFA6B0C4"), FontSize = 12, IsHitTestVisible = false };
            SetLeft(label, 14); SetTop(label, y + 6);
            Children.Add(label);

            var captured = pd;
            var editor = PropEditors.Build(pd, data, ctx.P, ctx.Cls, ctx.M, InlineWidth(pd), structural => ctx.PropChanged(this, captured.Name, structural));
            SetLeft(editor, w - 14 - InlineWidth(pd)); SetTop(editor, y + 3);
            Children.Add(editor);
            y += InlineH;
        }

        // ---------- пины ----------
        if (compact) AddPin(outputs[0], 1.5 + (HeaderH - 1.5) / 2, ctx);
        for (int i = 0; i < rows; i++)
        {
            double cy = pinsTop + i * RowH + RowH / 2;
            if (i < inputs.Count) AddPin(inputs[i], cy, ctx);
            if (i < outputs.Count) AddPin(outputs[i], cy, ctx);
        }
    }

    static double InlineWidth(PropDef pd) => pd.Name == "Template" ? 190 : pd.Name is "Var" or "Name" ? 90 : 130;

    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    static bool HasEditor(Pin p) => p.Type is "bool" or "int" or "long" or "double" or "decimal" or "string" or "char" or "object";

    public bool IsSelected
    {
        set
        {
            _frame.BorderBrush = CodeRender.Solid(value ? "#FF56B4E9" : "#FF4A5266");
            _frame.BorderThickness = new Thickness(value ? 2.4 : 1.5);
            _shadow.Color = value ? (Color)ColorConverter.ConvertFromString("#FF56B4E9") : Colors.Black;
            _shadow.Opacity = value ? 0.7 : 0.5;
            _shadow.BlurRadius = value ? 22 : 16;
        }
    }

    public void SetError(bool on)
    {
        if (!on) return;
        _frame.BorderBrush = CodeRender.Solid("#FFE9573F");
        _frame.BorderThickness = new Thickness(2.2);
    }

    // ---------------- пины ----------------
    void AddPin(Pin pin, double cy, NodeVisualContext ctx)
    {
        double x = pin.IsOutput ? NodeWidth : 0;
        Anchors[Key(pin)] = new Point(x, cy);

        FrameworkElement dot = pin.IsExec ? MakeExecDot(false) : MakeDataDot(pin.Type, false);
        dot.Cursor = Cursors.Cross;
        dot.Tag = new PinRef(Data.Id, pin.Id, pin.IsOutput, pin.IsExec, pin.Type);
        dot.ToolTip = PinTip(pin);
        SetLeft(dot, x - 8); SetTop(dot, cy - 8);
        dot.MouseLeftButtonDown += (s, e) => PinMouseDown?.Invoke(this, pin, e);
        Children.Add(dot);
        Dots[Key(pin)] = dot;

        var label = new TextBlock
        {
            Text = pin.Label, Foreground = CodeRender.Solid("#FFDDE2EE"), FontSize = LabelSize, IsHitTestVisible = false,
        };
        double lw = TextWidth(pin.Label, LabelSize);
        if (pin.IsOutput) SetLeft(label, x - 18 - lw - 2);
        else SetLeft(label, 18);
        SetTop(label, cy - 10);
        Children.Add(label);

        // поле значения для неподключённого входа: рядом с подписью
        if (!pin.IsOutput && !pin.IsExec && !ctx.ConnectedInputs.Contains(pin.Id))
        {
            var editor = MakePinEditor(pin, ctx);
            if (editor is not null)
            {
                SetLeft(editor, 18 + lw + 10);
                SetTop(editor, cy - 12);
                Children.Add(editor);
            }
        }
    }

    static string PinTip(Pin pin) =>
        pin.IsExec
            ? (pin.IsOutput ? "Порядок выполнения: что произойдёт ДАЛЬШЕ.\nТяните отсюда к белому входу следующей ноды." : "Порядок выполнения: сюда приходит управление.\nТяните сюда от белого выхода предыдущей ноды.")
            : (pin.IsOutput ? $"Выход данных ({TypeName(pin.Type)}).\nТяните к входу другой ноды — значение уйдёт туда." : $"Вход данных ({TypeName(pin.Type)}).\nНе подключён — берётся значение из поля рядом.");

    static string TypeName(string t) => t switch
    {
        "int" => "int — целое число", "long" => "long — большое целое", "double" => "double — дробное число", "decimal" => "decimal — деньги",
        "bool" => "bool — да/нет", "string" => "string — текст", "char" => "char — один символ", "object" => "любое значение",
        _ => t,
    };

    public static FrameworkElement MakeExecDot(bool filled) => new Polygon
    {
        Points = new PointCollection { new(0, 0), new(9, 0), new(16, 8), new(9, 16), new(0, 16) },
        Fill = filled ? Brushes.White : CodeRender.Solid("#FF262A35"),
        Stroke = Brushes.White, StrokeThickness = 1.8, Width = 16, Height = 16, Stretch = Stretch.None,
    };

    public static FrameworkElement MakeDataDot(string type, bool filled)
    {
        var c = CodeRender.Solid(GraphTypes.Color(type));
        return new Ellipse
        {
            Width = 15, Height = 15, Fill = filled ? c : CodeRender.Solid("#FF262A35"), Stroke = c, StrokeThickness = 2.2,
        };
    }

    /// <summary>Закрасить точку пина (подключён/нет).</summary>
    public void SetPinFilled(Pin pin, bool filled)
    {
        if (!Dots.TryGetValue(Key(pin), out var dot)) return;
        var color = pin.IsExec ? Brushes.White : CodeRender.Solid(GraphTypes.Color(pin.Type));
        if (dot is Shape s) s.Fill = filled ? color : CodeRender.Solid("#FF262A35");
    }

    FrameworkElement? MakePinEditor(Pin pin, NodeVisualContext ctx)
    {
        string current = Data.Props.TryGetValue("in:" + pin.Id, out var v) ? v : pin.Default;

        if (pin.Type == "bool")
        {
            var cb = new CheckBox
            {
                IsChecked = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase),
                VerticalAlignment = VerticalAlignment.Center, Content = "да", Foreground = CodeRender.Solid("#FFDDE2EE"), FontSize = 12.5,
            };
            cb.Click += (s, e) =>
            {
                Data.Props["in:" + pin.Id] = cb.IsChecked == true ? "true" : "false";
                ctx.PinValueChanged(this);
            };
            return cb;
        }

        if (HasEditor(pin))
        {
            var tb = PropEditors.Box(current, EditorWidth(pin.Type));
            tb.ToolTip = "Значение, пока вход не подключён проводом";
            tb.TextChanged += (s, e) =>
            {
                Data.Props["in:" + pin.Id] = tb.Text;
                ctx.PinValueChanged(this);
            };
            return tb;
        }
        return null;
    }
}
