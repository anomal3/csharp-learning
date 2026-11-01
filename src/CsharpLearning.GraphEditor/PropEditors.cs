using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Редакторы свойств ноды: общие для самой ноды (короткие поля) и для панели свойств (все поля).</summary>
public static class PropEditors
{
    public static TextBox Box(string text, double width) => new()
    {
        Text = text, Width = width, Height = 24, FontSize = 13, Padding = new Thickness(6, 1, 6, 1),
        Background = CodeRender.Solid("#FF14161D"), Foreground = CodeRender.Solid("#FFE9ECF2"),
        BorderBrush = CodeRender.Solid("#FF3A4152"), CaretBrush = CodeRender.Solid("#FF56B4E9"),
        VerticalContentAlignment = VerticalAlignment.Center, SelectionBrush = CodeRender.Solid("#FF56B4E9"),
    };

    public static ComboBox Combo(IEnumerable<string> items, string value, bool editable, double width, Action<string> onChanged)
    {
        var list = items.ToList();
        if (!string.IsNullOrEmpty(value) && !list.Contains(value)) list.Insert(0, value);
        var cb = new ComboBox { ItemsSource = list, IsEditable = editable, Width = width, Height = 26, SelectedItem = value, Text = value, FontSize = 13 };

        bool ready = false;
        cb.Loaded += (s, e) => ready = true;
        cb.SelectionChanged += (s, e) =>
        {
            if (ready && cb.SelectedItem is string v) onChanged(v);
        };
        if (editable)
            cb.LostFocus += (s, e) =>
            {
                if (ready && !string.IsNullOrWhiteSpace(cb.Text) && cb.Text != value) onChanged(cb.Text.Trim());
            };
        return cb;
    }

    /// <summary>
    /// Редактор одного свойства. <paramref name="changed"/> получает «нужно ли перерисовать ноду»
    /// (структурное изменение: поменялись пины). Значение уже записано в node.Props.
    /// </summary>
    public static FrameworkElement Build(PropDef pd, NodeData node, GraphProject p, ClassDef cls, MethodDef m, double width, Action<bool> changed)
    {
        var nctx = new NodeCtx(node, p, cls, m);
        string value = nctx.Prop(pd.Name);

        void Commit(string newValue)
        {
            if (node.Props.TryGetValue(pd.Name, out var old) && old == newValue) return;
            node.Props[pd.Name] = newValue;
            changed(true);
        }

        switch (pd.Editor)
        {
            case PropEditor.Text:
            {
                var tb = Box(value, width);
                string atFocus = value;
                tb.GotFocus += (s, e) => atFocus = tb.Text;
                tb.TextChanged += (s, e) => { node.Props[pd.Name] = tb.Text; changed(false); };
                tb.LostFocus += (s, e) => { if (tb.Text != atFocus) { atFocus = tb.Text; changed(true); } };
                tb.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter && tb.Text != atFocus) { atFocus = tb.Text; changed(true); }
                };
                return tb;
            }
            case PropEditor.Bool:
            {
                var cb = new CheckBox { IsChecked = value == "true", VerticalAlignment = VerticalAlignment.Center, Content = pd.Label, Foreground = CodeRender.Solid("#FFE9ECF2"), FontSize = 13 };
                cb.Click += (s, e) => Commit(cb.IsChecked == true ? "true" : "false");
                return cb;
            }
            case PropEditor.Choice:
                return Combo(pd.Choices ?? Array.Empty<string>(), value, false, width, Commit);

            case PropEditor.Type:
            {
                var items = new List<string>(GraphTypes.Primitive) { "object" };
                items.AddRange(GraphTypes.Primitive.Select(t => $"List<{t}>"));
                foreach (var c in p.Classes.Where(c => !c.IsStatic)) { items.Add(c.Name); items.Add($"List<{c.Name}>"); }
                return Combo(items, value, true, width, Commit);
            }
            case PropEditor.Class:
                return Combo(p.Classes.Select(c => c.Name).ToList(), value, false, width, v =>
                {
                    // новый класс — сбросить зависимые выбранные метод и поле
                    var target = p.FindClass(v);
                    if (NodeSpecs.Get(node.Kind)?.Props.Any(x => x.Name == "Method") == true) node.Props["Method"] = target?.Methods.FirstOrDefault(x => !x.IsCtor)?.Name ?? "";
                    if (NodeSpecs.Get(node.Kind)?.Props.Any(x => x.Name == "Field") == true) node.Props["Field"] = target?.Fields.FirstOrDefault()?.Name ?? "";
                    Commit(v);
                });
            case PropEditor.Method:
            {
                var names = new List<string>();
                for (var c = p.FindClass(nctx.Prop("Class")); c is not null; c = p.FindClass(c.BaseClass))
                {
                    names.AddRange(c.Methods.Where(x => !x.IsCtor).Select(x => x.Name));
                    if (c.BaseClass is null) break;
                }
                return Combo(names.Distinct().ToList(), value, false, width, Commit);
            }
            case PropEditor.Field:
            {
                var names = new List<string>();
                for (var c = p.FindClass(nctx.Prop("Class")); c is not null; c = p.FindClass(c.BaseClass))
                {
                    names.AddRange(c.Fields.Select(x => x.Name));
                    if (c.BaseClass is null) break;
                }
                return Combo(names.Distinct().ToList(), value, false, width, Commit);
            }
            case PropEditor.Var:
                return Combo(GraphScope.Vars(p, cls, m).Select(v => v.Name).Distinct().ToList(), value, false, width, Commit);
        }
        return new TextBlock();
    }
}
