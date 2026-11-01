using System.Windows;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace CsharpLearning.App;

public partial class MainWindow : Window
{
    MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var state = App.State;
        _vm = new MainViewModel(state);
        DataContext = _vm;

        BuildModeTabs();

        _vm.PropertyChanged += (_, args) =>
        {
            // новый урок: обновить вкладки «Окно» и «Граф», если они сейчас открыты
            if (args.PropertyName == nameof(MainViewModel.CurrentLesson)) RefreshModeContent();

            // новый урок всегда открывается с самого верха, а не там, где закончили читать прошлый
            if (args.PropertyName == nameof(MainViewModel.CurrentLesson))
                Dispatcher.BeginInvoke(() => LessonScroll.ScrollToVerticalOffset(0), System.Windows.Threading.DispatcherPriority.Loaded);

            // расширенный режим показывается в правой панели — раскрываем её, если она свёрнута
            if (args.PropertyName == nameof(MainViewModel.ShowAdvanced) && _vm.ShowAdvanced && !_rightOpen)
                OnToggleRight(this, new RoutedEventArgs());
        };

        // Первый урок открывается сам: приложение сразу готово к чтению.
        _vm.OpenNextLesson();

        // служебные ключи для проверки: --tag=N (первый урок с N-м тегом словаря), --mode=window|graph
        foreach (var a in Environment.GetCommandLineArgs())
        {
            if (a.StartsWith("--tag=") && int.TryParse(a[6..], out var ti) && ti >= 0 && ti < CsharpLearning.Core.Pedagogy.TagMapper.All.Count)
            {
                var tag = CsharpLearning.Core.Pedagogy.TagMapper.All[ti];
                var card = _vm.LessonCards.FirstOrDefault(c => c.Tags.Contains(tag));
                if (card is not null) _vm.OpenLesson(card);
            }
            else if (a.StartsWith("--mode=")) SetMode(a[7..]);
        }

        // Офлайн-режим не зависит от сети: проверку обновления делаем в фоне, учёбе не мешаем.
        await _vm.CheckUpdateIfDueAsync();
        if (_vm.AutoUpdateEnabled) await OfferAppUpdateAsync(manual: false);
    }

    /// <summary>Ищет новую версию программы; при согласии скачивает установщик, запускает его и закрывает приложение.</summary>
    async Task<bool> OfferAppUpdateAsync(bool manual)
    {
        var info = await Services.UpdateService.FetchAppUpdateAsync();
        if (info is null) return false;

        var ask = MessageBox.Show(this,
            $"Вышла новая версия программы: {info.Version.ToString(3)} (у вас {Services.UpdateService.CurrentVersion.ToString(3)}).\nУстановить сейчас? Прогресс и ответы сохранятся, приложение перезапустится само.",
            "CSharpCoach — обновление", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return true;

        var (path, error) = await Services.UpdateService.DownloadInstallerAsync(info);
        if (path is null) { MessageBox.Show(this, error, "CSharpCoach — обновление", MessageBoxButton.OK, MessageBoxImage.Warning); return true; }

        Services.UpdateService.LaunchInstaller(path);
        Application.Current.Shutdown();
        return true;
    }

    async void OnCheckUpdates(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _vm.IsUpdating) return;
        if (await OfferAppUpdateAsync(manual: true)) return;
        var message = await _vm.CheckAndUpdateContentAsync();
        MessageBox.Show(this, message, "CSharpCoach — обновление", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Выбор урока в оглавлении — сразу открываем его.</summary>
    void OnLessonSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is LessonCard card)
            _vm?.OpenLesson(card);
    }

    // ---------- вкладки урока: Консоль | Окно | Граф ----------
    string _mode = "console";

    void BuildModeTabs()
    {
        ModeTabs.Children.Clear();
        foreach (var (key, title, tip) in new[]
        {
            ("console", "Консольное приложение", "Примеры в чёрной консоли: текст урока и вопросы справа"),
            ("window", "Оконное (WinForms)", "Те же идеи в окне с кнопками, полями и картинками"),
            ("graph", "Граф", "Та же программа, собранная из нод"),
        })
        {
            bool on = key == _mode;
            var label = new TextBlock { Text = title, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = (System.Windows.Media.Brush)FindResource(on ? "Text" : "TextMuted") };
            var tab = new Border
            {
                Child = label, Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 6, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = tip,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Background = (System.Windows.Media.Brush)FindResource(on ? "CardBrush" : "BgSoft"),
                BorderBrush = (System.Windows.Media.Brush)FindResource(on ? "Accent" : "Line"),
                BorderThickness = new Thickness(0, on ? 3 : 0, 0, 0),
            };
            string k = key;
            tab.MouseLeftButtonUp += (s, e) => SetMode(k);
            ModeTabs.Children.Add(tab);
        }
    }

    void SetMode(string mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        BuildModeTabs();
        WindowTab.Visibility = mode == "window" ? Visibility.Visible : Visibility.Collapsed;
        GraphTab.Visibility = mode == "graph" ? Visibility.Visible : Visibility.Collapsed;
        RefreshModeContent();
    }

    void RefreshModeContent()
    {
        var lesson = _vm?.CurrentLesson?.Model;
        if (_mode == "window") { WindowTab.SetLesson(lesson); WindowTab.ScrollToTop(); }
        else if (_mode == "graph") { GraphTab.SetLesson(lesson); GraphTab.ScrollToTop(); }
    }

    void OnOpenGraph(object sender, RoutedEventArgs e)
    {
        var win = new CsharpLearning.GraphEditor.GraphEditorWindow { Owner = this };
        win.Show();
    }

    // ---------- сворачиваемые боковые панели ----------
    double _leftWidth = 330, _rightWidth = 470;
    bool _leftOpen = true, _rightOpen = true;

    void OnToggleLeft(object sender, RoutedEventArgs e)
    {
        if (_leftOpen)
        {
            _leftWidth = Math.Max(200, LeftCol.ActualWidth);
            LeftCol.MinWidth = 0;
            LeftCol.Width = new GridLength(0);
            LeftSplitCol.Width = new GridLength(0);
            LeftPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            LeftCol.MinWidth = 200;
            LeftCol.Width = new GridLength(_leftWidth);
            LeftSplitCol.Width = new GridLength(5);
            LeftPanel.Visibility = Visibility.Visible;
        }
        _leftOpen = !_leftOpen;
        ToggleLeftBtn.Content = _leftOpen ? "◧ Оглавление" : "▶ Оглавление";
    }

    void OnToggleRight(object sender, RoutedEventArgs e)
    {
        if (_rightOpen)
        {
            _rightWidth = Math.Max(320, RightCol.ActualWidth);
            RightCol.MinWidth = 0;
            RightCol.Width = new GridLength(0);
            RightSplitCol.Width = new GridLength(0);
            RightPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            RightCol.MinWidth = 320;
            RightCol.Width = new GridLength(_rightWidth);
            RightSplitCol.Width = new GridLength(5);
            RightPanel.Visibility = Visibility.Visible;
        }
        _rightOpen = !_rightOpen;
        ToggleRightBtn.Content = _rightOpen ? "Пример ◨" : "◀ Пример";
    }

    // ---------- подсказки при вводе ответа («IntelliSense» для вывода программы) ----------
    string? _suggestSource;
    List<string> _candidates = new();
    int _replaceStart, _replaceLength;
    bool _applying;

    /// <summary>Что вероятно появится в выводе: строки и числа прямо из кода примера.</summary>
    static List<string> ExtractCandidates(string code)
    {
        var result = new List<string>();
        void Add(string t)
        {
            t = t.Trim('\r', '\n');
            if (t.Length > 0 && t.Length <= 80 && !result.Contains(t)) result.Add(t);
        }

        foreach (Match m in Regex.Matches(code, "\\$?@?\"((?:\\\\.|[^\"\\\\])*)\""))
        {
            string lit = m.Groups[1].Value.Replace("\\n", "\n").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (!lit.Contains('{')) { foreach (var line in lit.Split('\n')) Add(line); continue; }
            foreach (var part in Regex.Split(lit, @"\{[^}]*\}")) if (part.Trim().Length > 1) Add(part);
        }
        foreach (Match m in Regex.Matches(code, @"(?<![\w.])\d+(?:\.\d+)?(?![\w.])")) Add(m.Value);
        if (Regex.IsMatch(code, @"\b(bool|true|false)\b|==|!=|[<>]=?")) { Add("True"); Add("False"); }
        Add("(ничего не выведено)");
        return result;
    }

    void RefreshCandidates()
    {
        string code = _vm?.CurrentLesson?.CurrentQuestion?.Code ?? "";
        if (code == _suggestSource) return;
        _suggestSource = code;
        _candidates = ExtractCandidates(code);
        CsharpLearning.Core.Runner.CodeCompletion.Warm(code);
    }

    sealed record SuggestItem(string Text, string Hint, int Start, int Length);

    int _suggestVersion;

    void OnAnswerTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_applying) return;
        _ = ShowSuggestionsAsync(forceAll: false);
    }

    async Task ShowSuggestionsAsync(bool forceAll)
    {
        RefreshCandidates();
        int version = ++_suggestVersion;

        int caret = AnswerBox.CaretIndex;
        string text = AnswerBox.Text;
        int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        string linePrefix = text.Substring(lineStart, caret - lineStart);

        int wordStart = caret;
        while (wordStart > lineStart && (char.IsLetterOrDigit(text[wordStart - 1]) || text[wordStart - 1] == '_')) wordStart--;
        string wordPrefix = text.Substring(wordStart, caret - wordStart);

        var items = new List<SuggestItem>();

        // 1) то, что вероятно появится в выводе: строки и числа из кода примера
        if (linePrefix.Length > 0 || forceAll)
            foreach (var c in _candidates)
                if (c.StartsWith(linePrefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(c, linePrefix, StringComparison.Ordinal))
                    items.Add(new SuggestItem(c, "из вывода", lineStart, linePrefix.Length));

        if (wordPrefix.Length > 0 && wordStart > lineStart)
            foreach (var c in _candidates)
                if (c.StartsWith(wordPrefix, StringComparison.OrdinalIgnoreCase) && !string.Equals(c, wordPrefix, StringComparison.Ordinal)
                    && !items.Any(m => m.Text == c))
                    items.Add(new SuggestItem(c, "из вывода", wordStart, wordPrefix.Length));

        // 2) код: типы, переменные примера и члены после точки (Roslyn)
        string code = _vm?.CurrentLesson?.CurrentQuestion?.Code ?? "";
        if (code.Length > 0 && (wordPrefix.Length > 0 || linePrefix.EndsWith('.')))
        {
            var codeItems = await CsharpLearning.Core.Runner.CodeCompletion.GetAsync(code, linePrefix);
            if (version != _suggestVersion) return; // пока считали, ввод уже изменился

            int start = linePrefix.EndsWith('.') ? caret : wordStart;
            int length = caret - start;
            foreach (var ci in codeItems)
                if (!items.Any(m => m.Text == ci.Text))
                    items.Add(new SuggestItem(ci.Text, ci.Hint, start, length));
        }

        if (items.Count == 0 || !AnswerBox.IsEnabled)
        {
            SuggestPopup.IsOpen = false;
            return;
        }

        var top = items.Take(10).ToList();
        SuggestList.Tag = top;
        SuggestList.ItemsSource = top;
        SuggestList.SelectedIndex = 0;

        var rect = AnswerBox.GetRectFromCharacterIndex(caret);
        SuggestPopup.HorizontalOffset = Math.Max(0, rect.Left);
        SuggestPopup.VerticalOffset = rect.IsEmpty ? AnswerBox.ActualHeight : rect.Bottom + 4;
        SuggestPopup.IsOpen = true;
    }

    void AcceptSuggestion()
    {
        if (!SuggestPopup.IsOpen || SuggestList.SelectedIndex < 0) return;
        if (SuggestList.Tag is not List<SuggestItem> items || SuggestList.SelectedIndex >= items.Count) return;
        var pick = items[SuggestList.SelectedIndex];

        _suggestVersion++;
        _applying = true;
        AnswerBox.Select(pick.Start, pick.Length);
        AnswerBox.SelectedText = pick.Text;
        AnswerBox.CaretIndex = pick.Start + pick.Text.Length;
        _applying = false;

        SuggestPopup.IsOpen = false;
        AnswerBox.Focus();
    }

    void OnAnswerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _ = ShowSuggestionsAsync(forceAll: true);
            e.Handled = true;
            return;
        }

        if (!SuggestPopup.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                SuggestList.SelectedIndex = Math.Min(SuggestList.Items.Count - 1, SuggestList.SelectedIndex + 1);
                e.Handled = true; break;
            case Key.Up:
                SuggestList.SelectedIndex = Math.Max(0, SuggestList.SelectedIndex - 1);
                e.Handled = true; break;
            case Key.Tab:
            case Key.Enter:
                AcceptSuggestion();
                e.Handled = true; break;
            case Key.Escape:
                SuggestPopup.IsOpen = false;
                e.Handled = true; break;
        }
    }

    void OnSuggestClick(object sender, MouseButtonEventArgs e)
    {
        var item = ItemsControl.ContainerFromElement(SuggestList, (DependencyObject)e.OriginalSource) as ListBoxItem;
        if (item is null) return;
        SuggestList.SelectedItem = item.DataContext;
        AcceptSuggestion();
        e.Handled = true;
    }

    void OnAnswerLostFocus(object sender, KeyboardFocusChangedEventArgs e) { _suggestVersion++; SuggestPopup.IsOpen = false; }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e) => _vm?.Dispose();
}
