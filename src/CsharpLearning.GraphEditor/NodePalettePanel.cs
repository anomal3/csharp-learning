using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Нода, которую сейчас тащат из палитры на холст.</summary>
public static class PaletteDrag
{
    public static PaletteItem? Current { get; set; }
    public const string Format = "csgraph/palette";
}

/// <summary>
/// Палитра нод слева: поиск и категории. Клик добавляет ноду в центр холста,
/// перетаскивание — туда, куда отпустили.
/// </summary>
public sealed class NodePalettePanel : Grid
{
    readonly TextBox _search = PropEditors.Box("", 100);
    readonly StackPanel _list = new();
    readonly HashSet<string> _collapsed = new();
    List<PaletteItem> _items = new();
    Point _dragStart;
    PaletteItem? _dragItem;

    public event Action<PaletteItem>? AddRequested;

    static readonly Dictionary<string, string> CategoryColor = new()
    {
        ["Поток"] = "#F5A623", ["Ввод-вывод"] = "#9B6AF5", ["Значения"] = "#7B8794", ["Переменные"] = "#4FC1E9",
        ["Математика"] = "#8CD867", ["Логика"] = "#E9573F", ["Текст"] = "#F6BB42", ["Списки"] = "#AC92EC", ["Классы проекта"] = "#48CFAD",
    };

    public NodePalettePanel()
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var head = new StackPanel { Margin = new Thickness(10, 4, 10, 8) };
        head.Children.Add(new TextBlock
        {
            Text = "Нажмите на ноду, чтобы добавить, или перетащите её на холст.",
            Foreground = CodeRender.Solid("#FF8C97AD"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 7),
        });
        _search.Width = double.NaN;
        _search.Height = 30;
        _search.ToolTip = "Поиск по названию: например «если», «цикл», «текст», «список»";
        _search.TextChanged += (s, e) => Rebuild();
        head.Children.Add(_search);
        Children.Add(head);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _list, Padding = new Thickness(6, 0, 6, 10) };
        SetRow(scroll, 1);
        Children.Add(scroll);
    }

    public void SetItems(List<PaletteItem> items)
    {
        _items = items;
        Rebuild();
    }

    void Rebuild()
    {
        _list.Children.Clear();
        var tokens = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToArray();
        bool searching = tokens.Length > 0;

        var filtered = _items.Where(i => !searching || tokens.All(t => (i.Title + " " + i.Category + " " + i.Hint).ToLowerInvariant().Contains(t)));
        foreach (var group in filtered.GroupBy(i => i.Category))
        {
            string cat = group.Key;
            bool open = searching || !_collapsed.Contains(cat);
            string color = CategoryColor.GetValueOrDefault(cat, "#7B8794");

            var head = new Border
            {
                Background = CodeRender.Solid("#FF2A2F3D"), CornerRadius = new CornerRadius(6), Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 7, 0, 3), Cursor = Cursors.Hand,
            };
            var row = new DockPanel();
            row.Children.Add(new Border { Width = 4, Height = 14, CornerRadius = new CornerRadius(2), Background = CodeRender.Solid(color), Margin = new Thickness(0, 0, 8, 0) });
            var arrow = new TextBlock { Text = open ? "▾" : "▸", Foreground = CodeRender.Solid("#FF8C97AD"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(arrow);
            row.Children.Add(new TextBlock { Text = cat, FontWeight = FontWeights.SemiBold, FontSize = 13, Foreground = CodeRender.Solid("#FFE9ECF2") });
            head.Child = row;
            head.MouseLeftButtonUp += (s, e) =>
            {
                if (!_collapsed.Remove(cat)) _collapsed.Add(cat);
                Rebuild();
            };
            _list.Children.Add(head);

            if (!open) continue;
            foreach (var item in group) _list.Children.Add(MakeRow(item, color));
        }

        if (_list.Children.Count == 0)
            _list.Children.Add(new TextBlock { Text = "Ничего не найдено", Foreground = CodeRender.Solid("#FF8C97AD"), Margin = new Thickness(12, 10, 0, 0), FontSize = 13 });
    }

    UIElement MakeRow(PaletteItem item, string color)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = item.Title, FontSize = 13, Foreground = CodeRender.Solid("#FFE3E7F0"), TextTrimming = TextTrimming.CharacterEllipsis });
        if (item.Hint.Length > 0)
            sp.Children.Add(new TextBlock
            {
                Text = item.Hint, FontSize = 11.5, Foreground = CodeRender.Solid("#FF8C97AD"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MaxHeight = 32,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

        var b = new Border
        {
            Child = sp, Padding = new Thickness(12, 6, 8, 6), Margin = new Thickness(6, 1, 0, 1), Cursor = Cursors.Hand,
            CornerRadius = new CornerRadius(5), Background = Brushes.Transparent, ToolTip = item.Hint.Length > 0 ? item.Hint : item.Title,
            BorderBrush = CodeRender.Solid(color), BorderThickness = new Thickness(2, 0, 0, 0),
        };
        b.MouseEnter += (s, e) => b.Background = CodeRender.Solid("#FF2A3550");
        b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;

        b.PreviewMouseLeftButtonDown += (s, e) => { _dragStart = e.GetPosition(null); _dragItem = item; };
        b.PreviewMouseMove += (s, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragItem != item) return;
            var p = e.GetPosition(null);
            if (Math.Abs(p.X - _dragStart.X) + Math.Abs(p.Y - _dragStart.Y) < 8) return;
            _dragItem = null;
            PaletteDrag.Current = item;
            DragDrop.DoDragDrop(b, new DataObject(PaletteDrag.Format, item.Title), DragDropEffects.Copy);
            PaletteDrag.Current = null;
        };
        b.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (_dragItem == item) { _dragItem = null; AddRequested?.Invoke(item); }
        };
        return b;
    }
}
