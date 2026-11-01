using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Панель «Что это за нода»: описание простыми словами и все настройки выбранной ноды.</summary>
public sealed class NodeInspector : Border
{
    readonly StackPanel _body = new() { Margin = new Thickness(14, 10, 14, 12) };

    public NodeInspector()
    {
        Background = CodeRender.Solid("#FF1F232D");
        BorderBrush = CodeRender.Solid("#FF3A4152");
        BorderThickness = new Thickness(0, 0, 0, 1);
        Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body, MaxHeight = 300 };
        ShowNothing();
    }

    public void ShowNothing(string? methodHint = null)
    {
        _body.Children.Clear();
        _body.Children.Add(Text("Свойства ноды", 14, "#FFE9ECF2", bold: true));
        _body.Children.Add(Text(methodHint ?? "Выберите ноду на холсте — здесь появятся её описание и настройки.", 12.5, "#FF8C97AD", top: 5));
    }

    /// <param name="changed">(нода, структурное ли изменение) — вызывается после правки свойства.</param>
    public void Show(NodeData node, GraphProject p, ClassDef cls, MethodDef m, Action<NodeData, bool> changed)
    {
        var spec = NodeSpecs.Get(node.Kind);
        if (spec is null) { ShowNothing(); return; }

        _body.Children.Clear();
        var nctx = new NodeCtx(node, p, cls, m);

        var head = new DockPanel();
        head.Children.Add(new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Background = CodeRender.Solid(spec.Color), Margin = new Thickness(0, 0, 10, 0),
            Child = new TextBlock { Text = spec.Icon, Foreground = CodeRender.Solid("#FF10131A"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold },
        });
        head.Children.Add(Text(spec.Title(nctx), 14, "#FFE9ECF2", bold: true));
        _body.Children.Add(head);

        string desc = NodeSpecs.Describe(node.Kind);
        if (desc.Length > 0) _body.Children.Add(Text(desc, 12.5, "#FF9AA4B8", top: 8));

        foreach (var pd in spec.Props)
        {
            var captured = pd;
            if (pd.Editor != PropEditor.Bool)
                _body.Children.Add(Text(pd.Label, 12, "#FFA6B0C4", top: 10));
            var editor = PropEditors.Build(pd, node, p, cls, m, 300, structural => changed(node, structural));
            editor.HorizontalAlignment = HorizontalAlignment.Left;
            editor.Margin = new Thickness(0, pd.Editor == PropEditor.Bool ? 10 : 3, 0, 0);
            _body.Children.Add(editor);
        }

        if (spec.Props.Count == 0)
            _body.Children.Add(Text("У этой ноды нет настроек: всё задаётся кружками и стрелками.", 12, "#FF7C879C", top: 8));
    }

    static TextBlock Text(string text, double size, string color, bool bold = false, double top = 0) => new()
    {
        Text = text, FontSize = size, Foreground = CodeRender.Solid(color), TextWrapping = TextWrapping.Wrap,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, top, 0, 0),
    };
}
