using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CsharpLearning.Core.Forms;

namespace CsharpLearning.App.View;

/// <summary>
/// Нарисованное «окно Windows Forms»: заголовок, рамка и элементы в привычном светлом виде.
/// Элементы живые — нажатия и ввод идут в FormRuntime, а его состояние снова рисуется на экране.
/// </summary>
public sealed class FormPreview : Border
{
    readonly FormRuntime _rt;
    readonly Canvas _client = new();
    readonly Grid _host = new();
    readonly Dictionary<string, FrameworkElement> _views = new();
    bool _syncing;

    static Brush B(string hex) => CsharpLearning.GraphEditor.CodeRender.Solid(hex);

    public FormPreview(FormRuntime rt)
    {
        _rt = rt;
        var spec = rt.Spec;

        Background = B("#FFF0F0F0");
        BorderBrush = B("#FF5A6A8A");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8, 8, 2, 2);
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, Opacity = 0.55, ShadowDepth = 4 };
        ClipToBounds = true;

        var root = new StackPanel();

        // ---- заголовок окна ----
        var title = new Grid { Background = B("#FFFFFFFF"), Height = 32 };
        title.Children.Add(new TextBlock
        {
            Text = spec.Title, Foreground = B("#FF1B1B1B"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(34, 0, 0, 0),
        });
        title.Children.Add(new Border
        {
            Width = 16, Height = 16, Margin = new Thickness(10, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = B("#FF4A90D9"), CornerRadius = new CornerRadius(3),
        });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var glyph in new[] { "—", "☐", "✕" })
            buttons.Children.Add(new TextBlock { Text = glyph, Width = 44, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = B("#FF1B1B1B"), FontSize = 12 });
        title.Children.Add(buttons);
        root.Children.Add(title);
        root.Children.Add(new Border { Height = 1, Background = B("#FFD0D0D0") });

        // ---- клиентская область ----
        _client.Width = spec.Width;
        _client.Height = spec.Height;
        _client.Background = B("#FFF0F0F0");
        _host.Children.Add(_client);
        root.Children.Add(_host);
        Child = root;

        foreach (var c in spec.Controls) BuildControl(c);

        rt.Changed += OnChanged;
        rt.MessageShown += ShowMessage;
        Unloaded += (s, e) => { rt.Changed -= OnChanged; rt.MessageShown -= ShowMessage; };
        ApplyAll();
    }

    // ---------------- элементы ----------------
    void BuildControl(ControlSpec c)
    {
        FrameworkElement el;
        switch (c.Kind)
        {
            case ControlKind.Label:
                el = new TextBlock { Foreground = Brushes.Black, FontSize = 12.5, TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Segoe UI"), LineHeight = double.NaN };
                break;

            case ControlKind.Button:
            {
                var btn = new Button { Width = c.W, Height = c.H, Content = c.Text, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, Cursor = System.Windows.Input.Cursors.Arrow };
                btn.Template = ButtonTemplate();
                btn.Click += (s, e) => _rt.Click(c.Name);
                el = btn;
                break;
            }
            case ControlKind.TextBox:
            {
                var tb = new TextBox
                {
                    Width = c.W, Height = c.H, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, Background = Brushes.White,
                    BorderBrush = B("#FF7A7A7A"), BorderThickness = new Thickness(1), Padding = new Thickness(2, 1, 2, 1),
                    VerticalContentAlignment = VerticalAlignment.Center, CaretBrush = Brushes.Black, SelectionBrush = B("#FF3399FF"),
                };
                tb.TextChanged += (s, e) => { if (!_syncing) _rt.UserTypes(c.Name, tb.Text); };
                el = tb;
                break;
            }
            case ControlKind.CheckBox:
            {
                var cb = new CheckBox { FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, VerticalContentAlignment = VerticalAlignment.Center };
                cb.Click += (s, e) => { if (!_syncing) _rt.UserChecks(c.Name, cb.IsChecked == true); };
                el = cb;
                break;
            }
            case ControlKind.ListBox:
                el = new ListBox
                {
                    Width = c.W, Height = c.H, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI"), Foreground = Brushes.Black, Background = Brushes.White,
                    BorderBrush = B("#FF828790"), BorderThickness = new Thickness(1),
                };
                break;

            case ControlKind.PictureBox:
                el = new Border { Width = c.W, Height = c.H, BorderBrush = B("#FF828790"), BorderThickness = new Thickness(1), Background = Brushes.White, ClipToBounds = true };
                break;

            case ControlKind.ProgressBar:
                el = new ProgressBar
                {
                    Style = null, Width = c.W, Height = c.H, Minimum = 0, Maximum = 100,
                    Foreground = B("#FF06B025"), Background = B("#FFE6E6E6"), BorderBrush = B("#FFBCBCBC"), BorderThickness = new Thickness(1),
                };
                break;

            default:
                el = new TextBlock();
                break;
        }

        el.Tag = c;
        Canvas.SetLeft(el, c.X);
        Canvas.SetTop(el, c.Y);
        _client.Children.Add(el);
        _views[c.Name] = el;
    }

    static ControlTemplate ButtonTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "b");
        border.SetValue(Border.BackgroundProperty, B("#FFE1E1E1"));
        border.SetValue(Border.BorderBrushProperty, B("#FFADADAD"));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(cp);
        t.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, B("#FFE5F1FB"), "b"));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, B("#FF0078D7"), "b"));
        var press = new Trigger { Property = System.Windows.Controls.Primitives.ButtonBase.IsPressedProperty, Value = true };
        press.Setters.Add(new Setter(Border.BackgroundProperty, B("#FFCCE4F7"), "b"));
        press.Setters.Add(new Setter(Border.BorderBrushProperty, B("#FF005499"), "b"));
        var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        off.Setters.Add(new Setter(Border.BackgroundProperty, B("#FFCCCCCC"), "b"));
        t.Triggers.Add(hover); t.Triggers.Add(press); t.Triggers.Add(off);
        return t;
    }

    // ---------------- состояние → экран ----------------
    void OnChanged(string name)
    {
        if (name == "*") ApplyAll();
        else Apply(name);
    }

    void ApplyAll()
    {
        foreach (var name in _views.Keys) Apply(name);
    }

    void Apply(string name)
    {
        if (!_views.TryGetValue(name, out var el)) return;
        _syncing = true;
        try
        {
            el.Visibility = _rt.VisibleOf(name) ? Visibility.Visible : Visibility.Hidden;
            el.IsEnabled = _rt.EnabledOf(name);

            switch (el)
            {
                case TextBlock tb: tb.Text = _rt.TextOf(name); break;
                case Button btn: btn.Content = Ink(_rt.TextOf(name)); break;
                case TextBox box: if (box.Text != _rt.TextOf(name)) box.Text = _rt.TextOf(name); break;
                case CheckBox cb: cb.Content = Ink(_rt.TextOf(name)); cb.IsChecked = _rt.CheckedOf(name); break;
                case ListBox lb:
                    lb.Items.Clear();
                    foreach (var item in _rt.ItemsOf(name)) lb.Items.Add(Ink(item));
                    if (lb.Items.Count > 0) lb.ScrollIntoView(lb.Items[^1]);
                    break;
                case ProgressBar pb: pb.Value = _rt.ProgressOf(name); break;
                case Border pic: pic.Child = PictureArt.Create(_rt.PictureOf(name)); break;
            }
        }
        finally { _syncing = false; }
    }

    /// <summary>Чёрный текст для светлого «окна»: общий стиль приложения красит TextBlock в белый, поэтому цвет задаём явно.</summary>
    static TextBlock Ink(string text) => new() { Text = text, Foreground = Brushes.Black, FontSize = 12.5, FontFamily = new FontFamily("Segoe UI") };

    // ---------------- MessageBox ----------------
    void ShowMessage(string text)
    {
        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), Width = _client.Width, Height = _client.Height };

        var ok = new Button { Content = Ink("OK"), Width = 76, Height = 26, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0), Template = ButtonTemplate(), Foreground = Brushes.Black };
        var body = new StackPanel { Margin = new Thickness(18, 14, 18, 14) };
        body.Children.Add(new TextBlock { Text = text, Foreground = Brushes.Black, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 260 });
        body.Children.Add(ok);

        var box = new Border
        {
            Background = Brushes.White, BorderBrush = B("#FF5A6A8A"), BorderThickness = new Thickness(1), Child = body,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MinWidth = 220,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, Opacity = 0.5 },
        };

        var layer = new Grid { Width = _client.Width, Height = _client.Height };
        layer.Children.Add(dim);
        layer.Children.Add(box);
        _host.Children.Add(layer);
        ok.Click += (s, e) => _host.Children.Remove(layer);
    }
}

/// <summary>Нарисованные «фотографии» для PictureBox: солнце и луна — без внешних файлов.</summary>
public static class PictureArt
{
    static Brush B(string hex) => CsharpLearning.GraphEditor.CodeRender.Solid(hex);

    public static UIElement? Create(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;

        var vb = new Viewbox { Stretch = Stretch.Uniform };
        var c = new Canvas { Width = 240, Height = 180 };
        vb.Child = c;

        if (id == "sun")
        {
            c.Background = new LinearGradientBrush(Color.FromRgb(0x6C, 0xC4, 0xF5), Color.FromRgb(0xCF, 0xEE, 0xFF), 90);
            for (int i = 0; i < 12; i++)
            {
                var ray = new Rectangle { Width = 8, Height = 26, Fill = B("#FFFFC83A"), RadiusX = 4, RadiusY = 4 };
                Canvas.SetLeft(ray, 116); Canvas.SetTop(ray, 12);
                ray.RenderTransform = new RotateTransform(i * 30, 4, 78);
                c.Children.Add(ray);
            }
            var sun = new Ellipse { Width = 84, Height = 84, Fill = B("#FFFFD84D"), Stroke = B("#FFFFB300"), StrokeThickness = 3 };
            Canvas.SetLeft(sun, 78); Canvas.SetTop(sun, 48);
            c.Children.Add(sun);
            var grass = new Rectangle { Width = 240, Height = 38, Fill = B("#FF5DBB63") };
            Canvas.SetTop(grass, 142);
            c.Children.Add(grass);
        }
        else
        {
            c.Background = new LinearGradientBrush(Color.FromRgb(0x14, 0x1B, 0x3C), Color.FromRgb(0x2B, 0x3A, 0x6B), 90);
            var rnd = new Random(7);
            for (int i = 0; i < 26; i++)
            {
                var star = new Ellipse { Width = 2 + rnd.Next(3), Height = 2 + rnd.Next(3), Fill = B("#FFFFFFFF"), Opacity = 0.5 + rnd.NextDouble() / 2 };
                Canvas.SetLeft(star, rnd.Next(10, 230)); Canvas.SetTop(star, rnd.Next(8, 130));
                c.Children.Add(star);
            }
            var moon = new Ellipse { Width = 80, Height = 80, Fill = B("#FFF4F1D6") };
            Canvas.SetLeft(moon, 80); Canvas.SetTop(moon, 46);
            c.Children.Add(moon);
            var bite = new Ellipse { Width = 66, Height = 66, Fill = B("#FF1D2750") };
            Canvas.SetLeft(bite, 104); Canvas.SetTop(bite, 38);
            c.Children.Add(bite);
            var ground = new Rectangle { Width = 240, Height = 34, Fill = B("#FF0F1830") };
            Canvas.SetTop(ground, 146);
            c.Children.Add(ground);
        }
        return vb;
    }
}
