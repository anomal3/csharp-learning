using System.Windows;

namespace CsharpLearning.App;

public partial class App : Application
{
    public static LearningState State { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Вложенные прокрутки (код в уроке, таблицы) не должны «съедать» колесо мыши:
        // отдаём прокрутку внешнему ScrollViewer.
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.DataGrid), UIElement.PreviewMouseWheelEvent,
            new System.Windows.Input.MouseWheelEventHandler(ForwardWheel));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.ScrollViewer), UIElement.PreviewMouseWheelEvent,
            new System.Windows.Input.MouseWheelEventHandler(ForwardWheel));
        State = LearningState.Create(e.Args);
        var win = new MainWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        MainWindow = win;
        win.Show();
        var graphArg = e.Args.FirstOrDefault(a => a.StartsWith("--graph"));
        if (graphArg is not null)
        {
            if (graphArg.StartsWith("--graph=") && int.TryParse(graphArg["--graph=".Length..], out var sample))
                CsharpLearning.GraphEditor.GraphEditorView.InitialSample = sample;
            new CsharpLearning.GraphEditor.GraphEditorWindow().Show();
        }
    }

    static void ForwardWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not UIElement el) return;

        // свой вертикальный скролл у элемента есть и он ещё не упёрся в край — пусть крутит сам
        if (el is System.Windows.Controls.ScrollViewer sv && sv.ScrollableHeight > 0 &&
            sv.VerticalScrollBarVisibility != System.Windows.Controls.ScrollBarVisibility.Disabled)
        {
            bool atTop = sv.VerticalOffset <= 0 && e.Delta > 0;
            bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight && e.Delta < 0;
            if (!atTop && !atBottom) return;
        }
        if (el is System.Windows.Controls.DataGrid) { /* таблицы всегда отдаём наружу */ }

        var parent = System.Windows.Media.VisualTreeHelper.GetParent((DependencyObject)el) as UIElement;
        if (parent is null) return;
        e.Handled = true;
        parent.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sender,
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        State?.Dispose();
        base.OnExit(e);
    }
}
