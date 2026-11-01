using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>
/// Холст с нодами одного метода: перемещение и масштаб, перетаскивание связей от пина к пину,
/// выделение рамкой, копирование, палитра по поиску. Данные лежат в GraphData метода —
/// холст только показывает их и правит.
/// </summary>
public sealed class NodeCanvas : Grid
{
    GraphProject? _p;
    ClassDef? _cls;
    MethodDef? _m;
    GraphData? _g;

    readonly Canvas _world = new();
    readonly Canvas _wireLayer = new();
    readonly Canvas _nodeLayer = new();
    readonly Canvas _overlay = new() { IsHitTestVisible = false };
    readonly MatrixTransform _xf = new(Matrix.Identity);
    readonly Dictionary<string, NodeVisual> _vis = new();
    readonly HashSet<string> _selected = new();
    readonly Dictionary<string, Matrix> _views = new();
    readonly PalettePopup _palette = new();
    static string? _clipboard;

    // состояние жестов
    PinRef? _wireFrom;
    Path? _tempWire;
    Point _wireMouse;
    bool _dragging;
    Point _dragStartWorld;
    Dictionary<string, Point> _dragOrigin = new();
    bool _dragMoved;
    bool _marquee;
    Point _marqueeStart;
    Rectangle? _marqueeRect;
    Point _panLast;
    bool _panMoved;
    PinRef? _pendingConnect;
    Point _pendingWorld;

    public event Action<string?>? SelectionChanged;
    /// <summary>Данные изменились — нужно пересобрать код.</summary>
    public event Action? Edited;
    /// <summary>Завершён осмысленный шаг: можно сохранить точку отмены.</summary>
    public event Action? Checkpoint;

    /// <summary>Режим просмотра: можно двигать вид и выбирать ноды, править граф нельзя (для иллюстраций в уроках).</summary>
    public bool ReadOnly { get; set; }

    /// <summary>Мельче этого масштаба «Показать всё» не уменьшает (иначе ноды нечитаемы). Для иллюстраций в уроках можно ниже.</summary>
    public double MinFitScale { get; set; } = 0.65;

    public GraphProject? Project => _p;
    public MethodDef? Method => _m;
    public IReadOnlyCollection<string> SelectedIds => _selected;

    public NodeCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        Background = MakeGridBrush();

        _world.RenderTransform = _xf;
        _world.Children.Add(_wireLayer);
        _world.Children.Add(_nodeLayer);
        Children.Add(_world);
        Children.Add(_overlay);

        MouseWheel += OnWheel;
        MouseLeftButtonDown += OnLeftDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonDown += OnRightDown;
        MouseRightButtonUp += OnRightUp;
        MouseDown += OnOtherDown;
        MouseUp += OnOtherUp;
        KeyDown += OnKey;
        LostMouseCapture += (s, e) => { if (_wireFrom is not null || _dragging || _marquee) CancelGestures(); };
        SizeChanged += (s, e) => { if (_pendingFit) FitAll(); };

        _palette.Chosen += OnPaletteChosen;

        AllowDrop = true;
        // (при ReadOnly падение ноды из палитры игнорируется в обработчике Drop)
        DragOver += (s, e) => { e.Effects = PaletteDrag.Current is null ? DragDropEffects.None : DragDropEffects.Copy; e.Handled = true; };
        Drop += (s, e) =>
        {
            if (!ReadOnly && PaletteDrag.Current is { } item) AddFromPalette(item, WorldOf(e.GetPosition(this)));
            e.Handled = true;
        };
    }

    // ===================== добавление нод извне (палитра слева) =====================

    /// <summary>Добавить ноду из палитры: в указанную точку мира или рядом с центром видимой области.</summary>
    public void AddFromPalette(PaletteItem item, Point? world = null)
    {
        if (_g is null || _p is null) return;
        _pendingConnect = null;
        _pendingWorld = world ?? FreeSpotNearCenter();
        OnPaletteChosen(item);
    }

    Point FreeSpotNearCenter()
    {
        var c = WorldOf(new Point(ActualWidth / 2 - 100, ActualHeight / 2 - 60));
        double x = Math.Round(c.X / 10) * 10, y = Math.Round(c.Y / 10) * 10;
        for (int i = 0; i < 30 && _g!.Nodes.Any(n => Math.Abs(n.X - x) < 60 && Math.Abs(n.Y - y) < 40); i++) { x += 40; y += 40; }
        return new Point(x, y);
    }

    /// <summary>Свойство ноды изменено в панели свойств.</summary>
    public void NodeEdited(string nodeId, bool structural)
    {
        Edited?.Invoke();
        if (!structural) return;
        RefreshNode(nodeId);
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    public NodeData? FindNode(string? id) => id is null ? null : _g?.Find(id);

    Brush MakeGridBrush()
    {
        var dots = new GeometryDrawing(
            CodeRender.Solid("#FF2B303C"), null, new EllipseGeometry(new Point(1, 1), 1.1, 1.1));
        var bg = new GeometryDrawing(CodeRender.Solid("#FF14161D"), null, new RectangleGeometry(new Rect(0, 0, 24, 24)));
        var group = new DrawingGroup();
        group.Children.Add(bg);
        group.Children.Add(dots);
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 24, 24), ViewboxUnits = BrushMappingMode.Absolute,
            Transform = _xf,
        };
        return brush;
    }

    // ===================== привязка к методу =====================
    bool _pendingFit;

    public void Bind(GraphProject p, ClassDef cls, MethodDef m)
    {
        if (_m is not null && _cls is not null) _views[_cls.Name + "." + _m.Name] = _xf.Matrix;

        _p = p; _cls = cls; _m = m; _g = m.Graph;
        _selected.Clear();
        CancelGestures();
        Rebuild();

        if (_views.TryGetValue(cls.Name + "." + m.Name, out var saved)) _xf.Matrix = saved;
        else { _xf.Matrix = Matrix.Identity; _pendingFit = true; if (ActualWidth > 0) FitAll(); }
        SelectionChanged?.Invoke(null);
    }

    public void Unbind()
    {
        _p = null; _cls = null; _m = null; _g = null;
        _vis.Clear(); _nodeLayer.Children.Clear(); _wireLayer.Children.Clear();
    }

    NodeVisualContext Ctx() => new()
    {
        P = _p!, Cls = _cls!, M = _m!,
        ConnectedInputs = _g!.Wires.Select(w => w.ToPin).ToHashSet(),
        PropChanged = OnPropChanged,
        PinValueChanged = _ => Edited?.Invoke(),
    };

    public void Rebuild()
    {
        _nodeLayer.Children.Clear();
        _wireLayer.Children.Clear();
        _vis.Clear();
        if (_g is null || _p is null) return;

        PruneWires();
        var ctx = Ctx();
        foreach (var n in _g.Nodes) AddVisual(n, ctx);
        RedrawWires();
        UpdateFills();
        foreach (var id in _selected) if (_vis.TryGetValue(id, out var v)) v.IsSelected = true;

        // в режиме просмотра поля ввода внутри нод выключены
        if (ReadOnly)
            foreach (var v in _vis.Values)
                foreach (var child in v.Children.OfType<FrameworkElement>())
                    if (child is TextBox or ComboBox or CheckBox) child.IsEnabled = false;
    }

    void AddVisual(NodeData n, NodeVisualContext ctx)
    {
        var spec = NodeSpecs.Get(n.Kind);
        if (spec is null) return;
        var pins = GraphScope.PinsOf(_p!, _cls!, _m!, n);
        var v = new NodeVisual(n, spec, pins, ctx);
        Canvas.SetLeft(v, n.X); Canvas.SetTop(v, n.Y);
        v.PinMouseDown += OnPinDown;
        v.PreviewMouseLeftButtonDown += OnNodeDown;
        _nodeLayer.Children.Add(v);
        _vis[n.Id] = v;
    }

    void RefreshNode(string id)
    {
        if (_g is null || !_vis.TryGetValue(id, out var old)) return;
        var n = _g.Find(id);
        if (n is null) return;
        _nodeLayer.Children.Remove(old);
        _vis.Remove(id);
        PruneWires();
        AddVisual(n, Ctx());
        // входы других нод могли потерять/получить провода — обновим все, кто связан с этой нодой
        foreach (var w in _g.Wires.Where(w => w.FromNode == id || w.ToNode == id).ToList())
        {
            var other = w.FromNode == id ? w.ToNode : w.FromNode;
            if (other != id && _vis.ContainsKey(other)) RebuildSilently(other);
        }
        RedrawWires();
        UpdateFills();
        if (_selected.Contains(id) && _vis.TryGetValue(id, out var nv)) nv.IsSelected = true;
    }

    void RebuildSilently(string id)
    {
        var n = _g!.Find(id);
        if (n is null || !_vis.TryGetValue(id, out var old)) return;
        _nodeLayer.Children.Remove(old);
        _vis.Remove(id);
        AddVisual(n, Ctx());
        if (_selected.Contains(id)) _vis[id].IsSelected = true;
    }

    void OnPropChanged(NodeVisual v, string prop, bool structural)
    {
        Edited?.Invoke();
        if (!structural) return;
        // перерисовать ноду после выхода из обработчика (нельзя ломать элемент, который сейчас шлёт событие)
        string id = v.Data.Id;
        Dispatcher.BeginInvoke(() => { RefreshNode(id); Edited?.Invoke(); Checkpoint?.Invoke(); }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ===================== связи =====================
    /// <summary>Убирает провода, которым больше не к чему подключаться (сменили тип, метод, класс…).</summary>
    public void PruneWires()
    {
        if (_g is null || _p is null) return;
        GraphScope.PruneWires(_p, _cls!, _m!);
    }

    void RedrawWires()
    {
        _wireLayer.Children.Clear();
        if (_g is null) return;

        foreach (var w in _g.Wires)
        {
            if (!_vis.TryGetValue(w.FromNode, out var a) || !_vis.TryGetValue(w.ToNode, out var b)) continue;
            if (!a.Anchors.TryGetValue(NodeVisual.Key(w.FromPin, true), out var pa) ||
                !b.Anchors.TryGetValue(NodeVisual.Key(w.ToPin, false), out var pb)) continue;

            var pin = a.Pins.FirstOrDefault(p => p.IsOutput && p.Id == w.FromPin);
            var color = pin is null || pin.IsExec ? "#FFFFFFFF" : GraphTypes.Color(pin.Type);
            var from = new Point(Canvas.GetLeft(a) + pa.X, Canvas.GetTop(a) + pa.Y);
            var to = new Point(Canvas.GetLeft(b) + pb.X, Canvas.GetTop(b) + pb.Y);
            var geo = Bezier(from, to);

            _wireLayer.Children.Add(new Path
            {
                Data = geo, Stroke = CodeRender.Solid(color), StrokeThickness = pin?.IsExec == true ? 2.6 : 2.2,
                IsHitTestVisible = false, StrokeLineJoin = PenLineJoin.Round,
            });
            _wireLayer.Children.Add(new Path { Data = geo, Stroke = Brushes.Transparent, StrokeThickness = 12, Tag = w, Cursor = Cursors.Hand });
        }
    }

    static Geometry Bezier(Point a, Point b)
    {
        double dx = Math.Max(60, Math.Abs(b.X - a.X) * 0.5);
        var fig = new PathFigure { StartPoint = a };
        fig.Segments.Add(new BezierSegment(new Point(a.X + dx, a.Y), new Point(b.X - dx, b.Y), b, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    void UpdateFills()
    {
        if (_g is null) return;
        foreach (var (id, v) in _vis)
            foreach (var pin in v.Pins)
            {
                bool filled = pin.IsOutput
                    ? _g.Wires.Any(w => w.FromNode == id && w.FromPin == pin.Id)
                    : _g.Wires.Any(w => w.ToNode == id && w.ToPin == pin.Id);
                v.SetPinFilled(pin, filled);
            }
    }

    // ---------- правила подключения ----------
    bool TryNormalize(PinRef a, PinRef b, out PinRef from, out PinRef to)
    {
        from = a; to = b;
        if (a.IsOutput == b.IsOutput) return false;
        if (!a.IsOutput) { from = b; to = a; }
        if (from.NodeId == to.NodeId) return false;
        if (from.IsExec != to.IsExec) return false;
        if (!from.IsExec && !GraphTypes.CanAssign(_p, from.Type, to.Type)) return false;
        if (from.IsExec) return !Reaches(to.NodeId, from.NodeId, execOnly: true);
        // данные: нельзя, чтобы источник уже зависел от получателя
        return !DependsOn(from.NodeId, to.NodeId);
    }

    bool Reaches(string start, string target, bool execOnly)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == target) return true;
            if (!seen.Add(cur)) continue;
            foreach (var w in _g!.Wires.Where(w => w.FromNode == cur))
            {
                var pin = _vis.TryGetValue(cur, out var v) ? v.Pins.FirstOrDefault(p => p.IsOutput && p.Id == w.FromPin) : null;
                if (pin?.IsExec == execOnly) stack.Push(w.ToNode);
            }
        }
        return false;
    }

    /// <summary>Зависит ли нода node от нод upstream через провода данных.</summary>
    bool DependsOn(string node, string upstream)
    {
        var seen = new HashSet<string>();
        var stack = new Stack<string>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (cur == upstream) return true;
            if (!seen.Add(cur)) continue;
            foreach (var w in _g!.Wires.Where(w => w.ToNode == cur))
            {
                var pin = _vis.TryGetValue(w.FromNode, out var v) ? v.Pins.FirstOrDefault(p => p.IsOutput && p.Id == w.FromPin) : null;
                if (pin is { IsExec: false }) stack.Push(w.FromNode);
            }
        }
        return false;
    }

    void Connect(PinRef from, PinRef to)
    {
        if (_g is null) return;
        if (!to.IsExec) _g.Wires.RemoveAll(w => w.ToNode == to.NodeId && w.ToPin == to.PinId);
        if (from.IsExec) _g.Wires.RemoveAll(w => w.FromNode == from.NodeId && w.FromPin == from.PinId);
        _g.Wires.Add(new WireData { FromNode = from.NodeId, FromPin = from.PinId, ToNode = to.NodeId, ToPin = to.PinId });

        if (!to.IsExec) RebuildSilently(to.NodeId);   // поле значения в ноде исчезает
        RedrawWires();
        UpdateFills();
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    void RemoveWire(WireData w)
    {
        if (_g is null) return;
        _g.Wires.Remove(w);
        if (_vis.ContainsKey(w.ToNode)) RebuildSilently(w.ToNode);
        RedrawWires();
        UpdateFills();
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    // ===================== мышь: пины и связи =====================
    void OnPinDown(NodeVisual v, Pin pin, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Focus();
        if (_g is null || ReadOnly) return;

        var start = new PinRef(v.Data.Id, pin.Id, pin.IsOutput, pin.IsExec, pin.Type);

        // потянули за подключённый вход — «отцепляем» провод и тащим его конец
        if (!pin.IsOutput)
        {
            var existing = _g.Wires.FirstOrDefault(w => w.ToNode == start.NodeId && w.ToPin == start.PinId);
            if (existing is not null && !pin.IsExec)
            {
                var srcNode = _vis.GetValueOrDefault(existing.FromNode);
                var srcPin = srcNode?.Pins.FirstOrDefault(p => p.IsOutput && p.Id == existing.FromPin);
                if (srcPin is not null)
                {
                    _g.Wires.Remove(existing);
                    RebuildSilently(start.NodeId);
                    start = new PinRef(existing.FromNode, existing.FromPin, true, srcPin.IsExec, srcPin.Type);
                    UpdateFills();
                }
            }
        }

        _wireFrom = start;
        _wireMouse = WorldOf(e.GetPosition(this));
        _tempWire = new Path { StrokeThickness = 2.4, Stroke = CodeRender.Solid(start.IsExec ? "#FFFFFFFF" : GraphTypes.Color(start.Type)), StrokeDashArray = new DoubleCollection { 4, 3 }, IsHitTestVisible = false };
        _wireLayer.Children.Add(_tempWire);
        UpdateTempWire();
        CaptureMouse();
    }

    Point PinWorld(PinRef r)
    {
        var v = _vis[r.NodeId];
        var a = v.Anchors[NodeVisual.Key(r.PinId, r.IsOutput)];
        return new Point(Canvas.GetLeft(v) + a.X, Canvas.GetTop(v) + a.Y);
    }

    void UpdateTempWire()
    {
        if (_wireFrom is null || _tempWire is null || !_vis.ContainsKey(_wireFrom.NodeId)) return;
        var p = PinWorld(_wireFrom);
        _tempWire.Data = _wireFrom.IsOutput ? Bezier(p, _wireMouse) : Bezier(_wireMouse, p);
    }

    PinRef? PinAt(Point world, PinRef from)
    {
        PinRef? best = null;
        double bestD = 22;
        foreach (var (id, v) in _vis)
        {
            foreach (var (key, dot) in v.Dots)
            {
                if (dot.Tag is not PinRef r) continue;
                if (!TryNormalize(from, r, out _, out _)) continue;
                var pw = PinWorld(r);
                double d = Math.Sqrt((pw.X - world.X) * (pw.X - world.X) + (pw.Y - world.Y) * (pw.Y - world.Y));
                if (d < bestD) { bestD = d; best = r; }
            }
        }
        return best;
    }

    // ===================== мышь: ноды, фон =====================
    void OnNodeDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not NodeVisual v || _g is null) return;
        Focus();

        // клики по полям ввода и пинам — не перетаскивание ноды
        for (var d = e.OriginalSource as DependencyObject; d is not null && d != v; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is TextBoxBase or ComboBox or ToggleButton or Popup || d is FrameworkElement { Tag: PinRef }) return;

        if (ReadOnly) { e.Handled = true; return; }

        bool add = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (!_selected.Contains(v.Data.Id))
        {
            if (!add) ClearSelection();
            Select(v.Data.Id);
        }
        else if (add) { Deselect(v.Data.Id); return; }

        _dragging = true;
        _dragMoved = false;
        _dragStartWorld = WorldOf(e.GetPosition(this));
        _dragOrigin = _selected.Where(id => _g.Find(id) is not null).ToDictionary(id => id, id => new Point(_g.Find(id)!.X, _g.Find(id)!.Y));
        CaptureMouse();
        e.Handled = true;
    }

    void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_g is null) return;
        // сюда доходят только клики мимо нод и пинов
        if (e.OriginalSource is Path { Tag: WireData }) return;

        if (e.ClickCount == 2 && !ReadOnly)
        {
            var pos = e.GetPosition(this);
            ShowPalette(pos, WorldOf(pos), null);
            return;
        }

        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) ClearSelection();
        _marquee = true;
        _marqueeStart = e.GetPosition(this);
        _marqueeRect = new Rectangle
        {
            Stroke = CodeRender.Solid("#FF56B4E9"), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 },
            Fill = CodeRender.Solid("#2256B4E9"), Width = 0, Height = 0,
        };
        _overlay.Children.Add(_marqueeRect);
        CaptureMouse();
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
        {
            if (_panLast == default) _panLast = pos;
            var dx = pos.X - _panLast.X; var dy = pos.Y - _panLast.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 0)
            {
                if (!_panMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
                _panMoved = true;
                var m = _xf.Matrix; m.Translate(dx, dy); _xf.Matrix = m;
                _panLast = pos;
            }
            return;
        }

        if (_wireFrom is not null)
        {
            _wireMouse = WorldOf(pos);
            UpdateTempWire();
            return;
        }

        if (_dragging && _g is not null)
        {
            var w = WorldOf(pos);
            double dx = w.X - _dragStartWorld.X, dy = w.Y - _dragStartWorld.Y;
            if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
            _dragMoved = true;
            foreach (var (id, o) in _dragOrigin)
            {
                var n = _g.Find(id)!;
                n.X = Math.Round((o.X + dx) / 10) * 10;
                n.Y = Math.Round((o.Y + dy) / 10) * 10;
                if (_vis.TryGetValue(id, out var v)) { Canvas.SetLeft(v, n.X); Canvas.SetTop(v, n.Y); }
            }
            RedrawWires();
            return;
        }

        if (_marquee && _marqueeRect is not null)
        {
            double x = Math.Min(pos.X, _marqueeStart.X), y = Math.Min(pos.Y, _marqueeStart.Y);
            Canvas.SetLeft(_marqueeRect, x); Canvas.SetTop(_marqueeRect, y);
            _marqueeRect.Width = Math.Abs(pos.X - _marqueeStart.X);
            _marqueeRect.Height = Math.Abs(pos.Y - _marqueeStart.Y);
        }
    }

    void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(this);

        if (_wireFrom is not null)
        {
            var from = _wireFrom;
            var world = WorldOf(pos);
            EndWireDrag();
            ReleaseMouseCapture();

            var target = PinAt(world, from);
            if (target is not null && TryNormalize(from, target, out var a, out var b)) Connect(a, b);
            else if (_vis.ContainsKey(from.NodeId) == false) { /* исходная нода пропала */ }
            else if (Math.Sqrt(Math.Pow(world.X - PinWorld(from).X, 2) + Math.Pow(world.Y - PinWorld(from).Y, 2)) > 30)
            {
                // отпустили в пустоте: предложить создать ноду, которая подойдёт к этому пину
                ShowPalette(pos, world, from);
            }
            else { Edited?.Invoke(); }
            return;
        }

        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            if (_dragMoved) { Edited?.Invoke(); Checkpoint?.Invoke(); }
            return;
        }

        if (_marquee)
        {
            _marquee = false;
            ReleaseMouseCapture();
            if (_marqueeRect is not null)
            {
                var r = new Rect(Canvas.GetLeft(_marqueeRect), Canvas.GetTop(_marqueeRect), _marqueeRect.Width, _marqueeRect.Height);
                _overlay.Children.Remove(_marqueeRect);
                _marqueeRect = null;
                if (r.Width > 4 || r.Height > 4)
                {
                    foreach (var (id, v) in _vis)
                    {
                        var tl = _xf.Matrix.Transform(new Point(Canvas.GetLeft(v), Canvas.GetTop(v)));
                        var s = _xf.Matrix.M11;
                        var box = new Rect(tl.X, tl.Y, v.NodeWidth * s, v.NodeHeight * s);
                        if (r.IntersectsWith(box)) Select(id);
                    }
                }
            }
        }
    }

    void OnRightDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        _panMoved = false;
        _panLast = e.GetPosition(this);
    }

    void OnRightUp(object sender, MouseButtonEventArgs e)
    {
        if (_panMoved || _g is null || ReadOnly) { _panMoved = false; return; }

        var pos = e.GetPosition(this);
        // на ноде — меню ноды, на проводе — меню провода, иначе — палитра
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
        {
            if (d is Path { Tag: WireData w }) { WireMenu(w); return; }
            if (d is NodeVisual nv) { NodeMenu(nv); return; }
        }
        ShowPalette(pos, WorldOf(pos), null);
    }

    void OnOtherDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle) { _panLast = e.GetPosition(this); _panMoved = false; }
    }

    void OnOtherUp(object sender, MouseButtonEventArgs e) { }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this);
        double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
        var m = _xf.Matrix;
        double s = m.M11 * factor;
        if (s < 0.25 || s > 2.2) return;
        m.ScaleAt(factor, factor, p.X, p.Y);
        _xf.Matrix = m;
        e.Handled = true;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (_g is null || ReadOnly || Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (e.Key is Key.Delete or Key.Back) { DeleteSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C) { Copy(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { Paste(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D) { Copy(); Paste(); e.Handled = true; }
        else if (ctrl && e.Key == Key.A) { foreach (var id in _vis.Keys) Select(id); e.Handled = true; }
        else if (e.Key == Key.F || e.Key == Key.Home) { FitAll(); e.Handled = true; }
        else if (e.Key == Key.Space)
        {
            var pos = Mouse.GetPosition(this);
            ShowPalette(pos, WorldOf(pos), null);
            e.Handled = true;
        }
    }

    void CancelGestures()
    {
        EndWireDrag();
        _dragging = false; _marquee = false;
        if (_marqueeRect is not null) { _overlay.Children.Remove(_marqueeRect); _marqueeRect = null; }
    }

    void EndWireDrag()
    {
        if (_tempWire is not null) _wireLayer.Children.Remove(_tempWire);
        _tempWire = null;
        _wireFrom = null;
        if (_g is not null && _vis.Count > 0) { RedrawWires(); UpdateFills(); }
    }

    // ===================== выделение =====================
    void Select(string id)
    {
        if (_selected.Add(id) && _vis.TryGetValue(id, out var v)) v.IsSelected = true;
        SelectionChanged?.Invoke(_selected.FirstOrDefault());
    }

    void Deselect(string id)
    {
        if (_selected.Remove(id) && _vis.TryGetValue(id, out var v)) v.IsSelected = false;
        SelectionChanged?.Invoke(_selected.FirstOrDefault());
    }

    void ClearSelection()
    {
        foreach (var id in _selected) if (_vis.TryGetValue(id, out var v)) v.IsSelected = false;
        _selected.Clear();
        SelectionChanged?.Invoke(null);
    }

    public void SelectNode(string? id, bool center = false)
    {
        ClearSelection();
        if (id is null || !_vis.TryGetValue(id, out var v)) return;
        Select(id);
        if (center)
        {
            var m = _xf.Matrix;
            double s = m.M11;
            m.OffsetX = ActualWidth / 2 - (Canvas.GetLeft(v) + v.NodeWidth / 2) * s;
            m.OffsetY = ActualHeight / 2 - (Canvas.GetTop(v) + v.NodeHeight / 2) * s;
            _xf.Matrix = m;
        }
    }

    public void MarkErrors(IEnumerable<string> nodeIds)
    {
        foreach (var id in nodeIds) if (_vis.TryGetValue(id, out var v) && !_selected.Contains(id)) v.SetError(true);
    }

    // ===================== удалить, копировать =====================
    public void DeleteSelected()
    {
        if (_g is null || _selected.Count == 0) return;
        foreach (var id in _selected.ToList())
        {
            var n = _g.Find(id);
            if (n is null || NodeSpecs.Get(n.Kind)?.Deletable != true) continue;
            _g.Nodes.Remove(n);
            _g.Wires.RemoveAll(w => w.FromNode == id || w.ToNode == id);
        }
        _selected.Clear();
        Rebuild();
        SelectionChanged?.Invoke(null);
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    sealed record Clip(List<NodeData> Nodes, List<WireData> Wires);

    void Copy()
    {
        if (_g is null) return;
        var ids = _selected.Where(id => NodeSpecs.Get(_g.Find(id)?.Kind ?? "")?.Deletable == true).ToHashSet();
        if (ids.Count == 0) return;
        var clip = new Clip(
            _g.Nodes.Where(n => ids.Contains(n.Id)).ToList(),
            _g.Wires.Where(w => ids.Contains(w.FromNode) && ids.Contains(w.ToNode)).ToList());
        _clipboard = JsonSerializer.Serialize(clip);
    }

    void Paste()
    {
        if (_g is null || _clipboard is null) return;
        var clip = JsonSerializer.Deserialize<Clip>(_clipboard);
        if (clip is null) return;

        var map = new Dictionary<string, string>();
        ClearSelection();
        foreach (var n in clip.Nodes)
        {
            var copy = new NodeData { Kind = n.Kind, X = n.X + 40, Y = n.Y + 40, Props = new Dictionary<string, string>(n.Props) };
            map[n.Id] = copy.Id;
            _g.Nodes.Add(copy);
        }
        foreach (var w in clip.Wires)
            _g.Wires.Add(new WireData { FromNode = map[w.FromNode], FromPin = w.FromPin, ToNode = map[w.ToNode], ToPin = w.ToPin });

        foreach (var id in map.Values) _selected.Add(id);
        Rebuild();
        SelectionChanged?.Invoke(_selected.FirstOrDefault());
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    // ===================== меню =====================
    void NodeMenu(NodeVisual v)
    {
        if (!_selected.Contains(v.Data.Id)) { ClearSelection(); Select(v.Data.Id); }
        var menu = new ContextMenu { Resources = Resources };
        void Item(string title, Action act, bool enabled = true)
        {
            var mi = new MenuItem { Header = title, IsEnabled = enabled };
            mi.Click += (s, e) => act();
            menu.Items.Add(mi);
        }
        bool deletable = v.Spec.Deletable;
        Item("Дублировать (Ctrl+D)", () => { Copy(); Paste(); }, deletable);
        Item("Отключить все провода", () =>
        {
            _g!.Wires.RemoveAll(w => w.FromNode == v.Data.Id || w.ToNode == v.Data.Id);
            Rebuild(); Edited?.Invoke(); Checkpoint?.Invoke();
        });
        Item("Удалить (Del)", DeleteSelected, deletable);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    void WireMenu(WireData w)
    {
        var menu = new ContextMenu { Resources = Resources };
        var mi = new MenuItem { Header = "Удалить провод" };
        mi.Click += (s, e) => RemoveWire(w);
        menu.Items.Add(mi);
        menu.PlacementTarget = this;
        menu.IsOpen = true;
    }

    // ===================== палитра нод =====================
    void ShowPalette(Point screen, Point world, PinRef? from)
    {
        if (_g is null || _p is null) return;
        _pendingConnect = from;
        _pendingWorld = world;

        var items = NodeSpecs.Palette(_p, _cls!, _m!);
        if (from is not null) items = items.Where(i => FindCompatiblePin(i, from) is not null).ToList();
        _palette.Show(this, screen, items, from is null ? "Добавить ноду" : "Нода для подключения");
    }

    string? FindCompatiblePin(PaletteItem item, PinRef from)
    {
        var temp = new NodeData { Id = "__tmp__", Kind = item.Kind };
        foreach (var kv in item.Props) temp.Props[kv.Key] = kv.Value;
        var pins = GraphScope.PinsOf(_p!, _cls!, _m!, temp);

        // для exec-пинов предпочитаем вход «in»
        foreach (var pin in pins.OrderByDescending(p => p.IsExec && p.Id == "in"))
        {
            if (pin.IsOutput == from.IsOutput || pin.IsExec != from.IsExec) continue;
            if (!from.IsExec)
            {
                var (outType, inType) = from.IsOutput ? (from.Type, pin.Type) : (pin.Type, from.Type);
                if (!GraphTypes.CanAssign(_p, outType, inType)) continue;
            }
            return pin.Id;
        }
        return null;
    }

    void OnPaletteChosen(PaletteItem item)
    {
        if (_g is null || _p is null) return;
        var node = new NodeData { Kind = item.Kind, X = Math.Round(_pendingWorld.X / 10) * 10, Y = Math.Round(_pendingWorld.Y / 10) * 10 };
        foreach (var kv in item.Props) node.Props[kv.Key] = kv.Value;
        AssignUniqueNames(node);
        _g.Nodes.Add(node);

        var from = _pendingConnect;
        _pendingConnect = null;

        Rebuild();
        ClearSelection();
        Select(node.Id);

        if (from is not null && _vis.ContainsKey(from.NodeId))
        {
            var pinId = FindCompatiblePin(item with { }, from);
            var pin = pinId is null ? null : _vis[node.Id].Pins.FirstOrDefault(p => p.Id == pinId && p.IsOutput != from.IsOutput);
            if (pin is not null)
            {
                var target = new PinRef(node.Id, pin.Id, pin.IsOutput, pin.IsExec, pin.Type);
                if (TryNormalize(from, target, out var a, out var b)) { Connect(a, b); return; }
            }
        }
        Edited?.Invoke();
        Checkpoint?.Invoke();
    }

    void AssignUniqueNames(NodeData n)
    {
        var existing = GraphScope.Vars(_p!, _cls!, _m!).Select(v => v.Name).ToHashSet();
        string Pick(string[] candidates, string fallback)
        {
            foreach (var c in candidates) if (!existing.Contains(c)) return c;
            for (int i = 2; ; i++) if (!existing.Contains(fallback + i)) return fallback + i;
        }

        switch (n.Kind)
        {
            case "ForRange": n.Props["Var"] = Pick(new[] { "i", "j", "k" }, "i"); break;
            case "ForEach": n.Props["Var"] = Pick(new[] { "item" }, "item"); break;
            case "DeclareVar": n.Props["Name"] = Pick(new[] { "x", "y", "z", "a", "b" }, "x"); break;
        }
    }

    // ===================== вид =====================
    Point WorldOf(Point screen)
    {
        var inv = _xf.Matrix;
        inv.Invert();
        return inv.Transform(screen);
    }

    public void FitAll()
    {
        if (_g is null || _g.Nodes.Count == 0 || ActualWidth < 50 || ActualHeight < 50) return;
        // пока окно не раскрылось до нормальных размеров, вид подгоняем заново при каждом изменении размера
        _pendingFit = ActualWidth < 600 || ActualHeight < 350;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in _g.Nodes)
        {
            double h = _vis.TryGetValue(n.Id, out var v) ? v.NodeHeight : 120;
            double nw = v?.NodeWidth ?? 260;
            minX = Math.Min(minX, n.X); minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + nw); maxY = Math.Max(maxY, n.Y + h);
        }

        double w = maxX - minX + 80, h2 = maxY - minY + 80;
        double fit = Math.Min(ActualWidth / w, ActualHeight / h2);

        // мельче 0.65 нода становится нечитаемой: в этом случае показываем начало графа в натуральном размере
        var m = Matrix.Identity;
        if (fit >= MinFitScale)
        {
            double sc = Math.Min(fit, 1.0);
            m.Scale(sc, sc);
            m.Translate((ActualWidth - (maxX - minX) * sc) / 2 - minX * sc, (ActualHeight - (maxY - minY) * sc) / 2 - minY * sc);
        }
        else
        {
            m.Scale(MinFitScale, MinFitScale);
            m.Translate(40 - minX * MinFitScale, 40 - minY * MinFitScale);
        }
        _xf.Matrix = m;
    }

    public void ResetZoom()
    {
        var m = _xf.Matrix;
        m.ScaleAt(1 / m.M11, 1 / m.M11, ActualWidth / 2, ActualHeight / 2);
        _xf.Matrix = m;
    }
}
