using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CsharpLearning.GraphEditor;

public enum FormKind { Text, Combo, EditableCombo, Check, Multi }

public sealed record FormField(string Key, string Label, FormKind Kind, string Value = "", IEnumerable<string>? Options = null, string Hint = "");

/// <summary>Маленькая тёмная форма «заполни поля»: для добавления класса, поля, метода.</summary>
public static class FormDialog
{
    public static Dictionary<string, string>? Show(Window? owner, string title, IEnumerable<FormField> fields,
        Func<Dictionary<string, string>, string?>? validate = null, string okText = "Готово")
    {
        var win = new Window
        {
            Title = title, Owner = owner, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height, Width = 460, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Background = CodeRender.Solid("#FF1B1D24"), Foreground = CodeRender.Solid("#FFE9ECF2"), FontSize = 13,
        };
        win.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CsharpLearning.GraphEditor;component/Themes/GraphDark.xaml") });

        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
        var getters = new Dictionary<string, Func<string>>();
        FrameworkElement? first = null;

        foreach (var f in fields)
        {
            panel.Children.Add(new TextBlock { Text = f.Label, Foreground = CodeRender.Solid("#FF9AA4B8"), FontSize = 12, Margin = new Thickness(0, 8, 0, 3) });
            FrameworkElement input;
            switch (f.Kind)
            {
                case FormKind.Check:
                {
                    var cb = new CheckBox { IsChecked = f.Value == "true", Content = f.Hint, Foreground = CodeRender.Solid("#FFE9ECF2") };
                    getters[f.Key] = () => cb.IsChecked == true ? "true" : "false";
                    input = cb;
                    break;
                }
                case FormKind.Combo:
                case FormKind.EditableCombo:
                {
                    var items = (f.Options ?? Array.Empty<string>()).ToList();
                    if (f.Value.Length > 0 && !items.Contains(f.Value)) items.Insert(0, f.Value);
                    var cb = new ComboBox { ItemsSource = items, IsEditable = f.Kind == FormKind.EditableCombo, Height = 28, Text = f.Value, SelectedItem = f.Value };
                    getters[f.Key] = () => (cb.IsEditable ? cb.Text : cb.SelectedItem as string ?? cb.Text) ?? "";
                    input = cb;
                    break;
                }
                case FormKind.Multi:
                {
                    var tb = Box(f.Value);
                    tb.AcceptsReturn = true; tb.Height = 70; tb.TextWrapping = TextWrapping.Wrap; tb.VerticalContentAlignment = VerticalAlignment.Top;
                    getters[f.Key] = () => tb.Text;
                    input = tb;
                    break;
                }
                default:
                {
                    var tb = Box(f.Value);
                    getters[f.Key] = () => tb.Text;
                    input = tb;
                    break;
                }
            }
            panel.Children.Add(input);
            if (f.Kind != FormKind.Check && f.Hint.Length > 0)
                panel.Children.Add(new TextBlock { Text = f.Hint, Foreground = CodeRender.Solid("#FF7C879C"), FontSize = 11, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap });
            first ??= input;
        }

        var error = new TextBlock { Foreground = CodeRender.Solid("#FFE9573F"), FontSize = 12, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        panel.Children.Add(error);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = okText, Width = 100, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.SetResourceReference(FrameworkElement.StyleProperty, "GPrimary");
        var cancel = new Button { Content = "Отмена", Width = 90, IsCancel = true };
        cancel.SetResourceReference(FrameworkElement.StyleProperty, "GButton");
        buttons.Children.Add(ok); buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        win.Content = panel;

        Dictionary<string, string>? result = null;
        ok.Click += (s, e) =>
        {
            var values = getters.ToDictionary(kv => kv.Key, kv => kv.Value());
            var msg = validate?.Invoke(values);
            if (msg is not null)
            {
                error.Text = msg;
                error.Visibility = Visibility.Visible;
                return;
            }
            result = values;
            win.DialogResult = true;
        };

        win.Loaded += (s, e) => first?.Focus();
        win.ShowDialog();
        return result;
    }

    static TextBox Box(string value) => new()
    {
        Text = value, Height = 28, Padding = new Thickness(6, 3, 6, 3), VerticalContentAlignment = VerticalAlignment.Center,
        Background = CodeRender.Solid("#FF14161D"), Foreground = CodeRender.Solid("#FFE9ECF2"),
        BorderBrush = CodeRender.Solid("#FF3A4152"), CaretBrush = CodeRender.Solid("#FF56B4E9"),
    };

    public static bool Confirm(Window? owner, string title, string message)
    {
        var r = Show(owner, title, new[] { new FormField("m", message, FormKind.Check, "true", null, "я понимаю") }, null, "Да, продолжить");
        return r is not null && r["m"] == "true";
    }
}
