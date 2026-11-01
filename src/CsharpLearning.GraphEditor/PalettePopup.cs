using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using CsharpLearning.Core.Graph;

namespace CsharpLearning.GraphEditor;

/// <summary>Окошко «что добавить на граф»: поиск по названию, стрелки и Enter.</summary>
public sealed class PalettePopup
{
    readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Relative, PopupAnimation = PopupAnimation.Fade };
    readonly TextBox _search;
    readonly ListBox _list;
    readonly TextBlock _title;
    List<PaletteItem> _all = new();

    public event Action<PaletteItem>? Chosen;

    public PalettePopup()
    {
        _title = new TextBlock { Foreground = CodeRender.Solid("#FF9AA4B8"), FontSize = 11.5, Margin = new Thickness(2, 0, 0, 5) };
        _search = new TextBox
        {
            Height = 28, FontSize = 13, Padding = new Thickness(7, 3, 7, 3), VerticalContentAlignment = VerticalAlignment.Center,
            Background = CodeRender.Solid("#FF14161D"), Foreground = CodeRender.Solid("#FFE9ECF2"),
            BorderBrush = CodeRender.Solid("#FF3A4152"), CaretBrush = CodeRender.Solid("#FF56B4E9"),
        };
        _list = new ListBox
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 330, Margin = new Thickness(0, 6, 0, 0),
            ItemContainerStyle = (Style)XamlReader.Parse("""
                <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ListBoxItem">
                                <Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" CornerRadius="4" Padding="7,4">
                                    <ContentPresenter/>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#FF2C4A63"/></Trigger>
                                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Background" Value="#FF2A3550"/></Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
                """),
        };

        _list.ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    <TextBlock Text="{Binding Title}" Foreground="#FFE9ECF2" FontSize="12.5" TextTrimming="CharacterEllipsis"/>
                    <TextBlock Grid.Column="1" Text="{Binding Category}" Foreground="#FF7C879C" FontSize="11" Margin="12,1,0,0"/>
                </Grid>
            </DataTemplate>
            """);

        var panel = new StackPanel { Width = 380 };
        panel.Children.Add(_title);
        panel.Children.Add(_search);
        panel.Children.Add(_list);

        _popup.Child = new Border
        {
            Background = CodeRender.Solid("#FF22283A"), BorderBrush = CodeRender.Solid("#FF56B4E9"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(10), Margin = new Thickness(0, 0, 14, 14),
            Child = panel,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, Opacity = 0.5, ShadowDepth = 3 },
        };

        _search.TextChanged += (s, e) => Refilter();
        _search.PreviewKeyDown += OnKey;
        _list.MouseDoubleClick += (s, e) => Pick();
        _list.PreviewMouseLeftButtonUp += (s, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, (DependencyObject)e.OriginalSource) is ListBoxItem) Pick();
        };
    }

    public void Show(UIElement target, Point pos, List<PaletteItem> items, string title)
    {
        _all = items;
        _title.Text = title + " — начни печатать для поиска";
        _search.Text = "";
        _popup.PlacementTarget = target;
        _popup.HorizontalOffset = Math.Max(0, pos.X);
        _popup.VerticalOffset = Math.Max(0, pos.Y);
        Refilter();
        _popup.IsOpen = true;
        _popup.Dispatcher.BeginInvoke(() => { _search.Focus(); Keyboard.Focus(_search); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    void Refilter()
    {
        var tokens = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.ToLowerInvariant()).ToArray();
        IEnumerable<PaletteItem> q = _all;
        if (tokens.Length > 0)
            q = q.Where(i => tokens.All(t => (i.Title + " " + i.Category + " " + i.Hint).ToLowerInvariant().Contains(t)))
                 .OrderByDescending(i => i.Title.ToLowerInvariant().StartsWith(tokens[0]));
        _list.ItemsSource = q.Take(80).ToList();
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: Move(1); e.Handled = true; break;
            case Key.Up: Move(-1); e.Handled = true; break;
            case Key.Enter: Pick(); e.Handled = true; break;
            case Key.Escape: _popup.IsOpen = false; e.Handled = true; break;
        }
    }

    void Move(int d)
    {
        if (_list.Items.Count == 0) return;
        _list.SelectedIndex = Math.Clamp(_list.SelectedIndex + d, 0, _list.Items.Count - 1);
        _list.ScrollIntoView(_list.SelectedItem);
    }

    void Pick()
    {
        if (_list.SelectedItem is not PaletteItem item) return;
        _popup.IsOpen = false;
        Chosen?.Invoke(item);
    }
}
