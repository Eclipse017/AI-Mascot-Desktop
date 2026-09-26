using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace AIMascot.ConsoleApp;

internal sealed class MainWindow : Window
{
    private readonly RoleController _controller = new();
    private readonly Dictionary<string, Card> _cards = new();
    private readonly List<Button> _buttons = new();
    private readonly Grid _cardGrid = new();
    private readonly ScrollViewer _scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly TextBlock _summary = Text("读取状态中…", 14, "#716B7F");
    private readonly TextBlock _message = Text("选择一位伙伴，或使用全部操作。", 13, "#716B7F");
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _refreshing, _busy;
    private sealed record Card(TextBlock Status, TextBlock Options, TextBlock Startup, TextBlock Result);

    public MainWindow(bool startCompanions = true)
    {
        Title = "AI 娘 · 统一控制台"; Width = Math.Min(1600, SystemParameters.WorkArea.Width - 48); Height = 840; MinWidth = 660; MinHeight = 520;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush("#F6F5F1"); Foreground = Brush("#302F39");
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        var body = new DockPanel { Margin = new Thickness(24, 20, 24, 16) };
        var header = new DockPanel();
        var badge = Text("LOCAL COMPANIONS", 11, "#716B7F"); badge.FontWeight = FontWeights.SemiBold;
        var titles = new StackPanel(); titles.Children.Add(badge);
        var title = Text($"{Role.Installed.Count} 位伙伴，一个控制台", 29, "#302F39"); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 5, 0, 8);
        titles.Children.Add(title); titles.Children.Add(_summary); header.Children.Add(titles); DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        var quick = new WrapPanel { Margin = new Thickness(0, 16, 0, 14) };
        foreach (var pair in new[] { ("全部启动", "start"), ("全部恢复", "restore"), ("全部隐藏", "hide"), ("全部跟随", "auto"), ("全部演示", "demo"), ("全部退出", "quit") })
            quick.Children.Add(Button(pair.Item1, () => Act(Role.Installed, pair.Item2), "全部 " + pair.Item1));
        DockPanel.SetDock(quick, Dock.Top); body.Children.Add(quick);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom);
        _message.Margin = new Thickness(2, 12, 2, 8); footer.Children.Add(_message);
        footer.Children.Add(Text("仅启动本地桌宠；不会打开 Claude 客户端、网页或调用模型。", 12, "#817D89"));
        footer.Children.Add(Text("关闭面板后伙伴继续陪伴。演示后可点「恢复显示」；跟随及全屏收起规则仍有效。", 12, "#817D89"));
        body.Children.Add(footer);
        for (int i = 0; i < Role.Installed.Count; i++)
        {
            var role = Role.Installed[i]; var card = CreateCard(role);
            card.Margin = new Thickness(5, 0, 5, 12); _cardGrid.Children.Add(card);
        }
        _scroll.Content = _cardGrid; body.Children.Add(_scroll); Content = body;
        _scroll.SizeChanged += (_, _) => ArrangeCards(); ArrangeCards();
        Loaded += async (_, _) => { await Refresh(); _timer.Start(); if (startCompanions) Act(Role.Installed, "start"); };
        _timer.Tick += async (_, _) => await Refresh();
        Closed += (_, _) => _timer.Stop();
    }

    private void ArrangeCards()
    {
        int columns = Math.Clamp((int)(Math.Max(1, _scroll.ActualWidth - 20) / 290), 1, Role.Installed.Count);
        if (_cardGrid.ColumnDefinitions.Count == columns) return;
        _cardGrid.ColumnDefinitions.Clear(); _cardGrid.RowDefinitions.Clear();
        for (int i = 0; i < columns; i++) _cardGrid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < (Role.Installed.Count + columns - 1) / columns; i++)
            _cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < _cardGrid.Children.Count; i++)
        { Grid.SetColumn(_cardGrid.Children[i], i % columns); Grid.SetRow(_cardGrid.Children[i], i / columns); }
    }

    private Border CreateCard(Role role)
    {
        var panel = new StackPanel { Margin = new Thickness(14) };
        var portrait = new Image { Source = new BitmapImage(new Uri($"pack://application:,,,/AIMascot.Console;component/Assets/{role.Id}.png")), Height = 62, Margin = new Thickness(0, 2, 0, 10) };
        RenderOptions.SetBitmapScalingMode(portrait, BitmapScalingMode.HighQuality); panel.Children.Add(portrait);
        var name = Text(role.Name, 22, role.Color); name.FontWeight = FontWeights.SemiBold; name.HorizontalAlignment = HorizontalAlignment.Center; panel.Children.Add(name);
        var binding = Text("跟随 " + role.Binding, 12, "#817D89"); binding.HorizontalAlignment = HorizontalAlignment.Center; binding.Margin = new Thickness(0, 5, 0, 14); panel.Children.Add(binding);
        var status = Text("读取状态中…", 15, "#302F39"); status.FontWeight = FontWeights.SemiBold;
        var options = Text("", 12, "#716B7F"); options.Margin = new Thickness(0, 6, 0, 0);
        var startup = Text("", 12, "#716B7F"); startup.Margin = new Thickness(0, 6, 0, 0);
        var state = new StackPanel(); state.Children.Add(status); state.Children.Add(options); state.Children.Add(startup);
        panel.Children.Add(new Border { Background = Brush("#F7F6FA"), Padding = new Thickness(12), CornerRadius = new CornerRadius(9), Child = state, MinHeight = 80 });
        var actions = new UniformGridCompat { Margin = new Thickness(0, 12, 0, 0) };
        foreach (var pair in new[] { ("启动待命", "start"), ("角色细项 ↗", "controls"), ("恢复显示", "restore"), ("手动隐藏", "hide"), ("跟随应用", "auto"), ("本地演示", "demo"), ("暂停 / 继续", "pause"), ("摸摸头", "pet"), ("打招呼", "greet"), ("休息 / 唤醒", "rest"), ("开启登录启动", "enable-startup"), ("关闭登录启动", "disable-startup") })
            actions.Children.Add(Button(pair.Item1, () => Act(new[] {role}, pair.Item2), role.Name + " " + pair.Item1, compact: true));
        panel.Children.Add(actions);
        panel.Children.Add(Button("退出这位伙伴", () => Act(new[] {role}, "quit"), role.Name + " 退出", compact: true));
        var result = Text("", 11, "#716B7F"); result.MinHeight = 30; result.Margin = new Thickness(0, 7, 0, 0); panel.Children.Add(result);
        _cards[role.Id] = new(status, options, startup, result);
        return new Border { Background = Brushes.White, CornerRadius = new CornerRadius(15), BorderBrush = Brush("#E8E5ED"), BorderThickness = new Thickness(1), Child = panel };
    }
    // Two equal columns stay aligned as status text changes.
    private sealed class UniformGridCompat : System.Windows.Controls.Primitives.UniformGrid
    { public UniformGridCompat() { Columns = 2; } }
    private Button Button(string label, Action action, string accessibility, bool compact = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(compact ? 5 : 13, 6, compact ? 5 : 13, 6), Margin = new Thickness(0, 0, 6, 4), MinHeight = 31,
            Background = Brush("#F5F3F8"), Foreground = Brush("#484151"), BorderBrush = Brush("#E6E1ED"), BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand };
        AutomationProperties.SetName(button, accessibility); button.Click += (_, _) => action(); _buttons.Add(button); return button;
    }
    private async void Act(IEnumerable<Role> roles, string command)
    {
        if (_busy) return; _busy = true; foreach (var button in _buttons) button.IsEnabled = false;
        _message.Text = "正在处理…"; int success = 0, total = 0;
        try
        {
            foreach (var role in roles)
            {
                var result = await _controller.ExecuteAsync(role, command); total++; if (result.Success) success++;
                _cards[role.Id].Result.Text = (result.Success ? "✓ " : "! ") + result.Message;
                _cards[role.Id].Result.Foreground = Brush(result.Success ? "#58765F" : "#A85842");
            }
            await Refresh(); _message.Text = $"已处理 {total} 位伙伴 · 成功 {success} · 未完成 {total-success}。";
        }
        finally { _busy = false; foreach (var button in _buttons) button.IsEnabled = true; }
    }
    internal async Task Refresh()
    {
        if (_refreshing) return; _refreshing = true;
        try
        {
            var states = await Task.Run(() => Role.Installed.Select(_controller.Read).ToArray());
            foreach (var state in states)
            {
                var card = _cards[state.Id];
                card.Status.Text = !state.Installed ? "尚未安装" : state.OtherCopy ? "检测到其他版本" : state.Error != null ? "状态暂时无法确认" : !state.Running ? "后台已停止 · 需启动" : state.Visible == true ? "陪伴中 · 已显示" : state.Reason switch {
                    "ManualHidden" => "已手动隐藏", "Suspended" => "锁屏或休眠中", "Fullscreen" => "全屏期间收起",
                    "WaitingForApp" => "等待对应应用启动", "Unknown" => "正在确认应用身份", _ => "运行中 · 已收起"
                };
                var p = state.Options;
                card.Options.Text = p == null ? "显示设置待确认" : (p.Mode == 0 ? "跟随应用" : "本地演示") + (p.UserHidden ? " · 手动隐藏" : "") + (p.Paused ? " · 动画暂停" : "");
                card.Startup.Text = "登录启动：" + (state.StartupEnabled == true ? "已开启" : state.StartupEnabled == false ? "已关闭" : "其他路径或无法确认");
            }
            _summary.Text = $"{states.Count(x => x.Running)} / {states.Length} 位伙伴运行中     ·     {states.Count(x => x.Visible == true)} 位已显示     ·     纯本地陪伴";
        }
        finally { _refreshing = false; }
    }
    internal void VerifyLayout(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new List<object>();
        foreach (var size in new[] { (1100d, 840d), (720d, 660d), (660d, 520d), (1600d, 900d) })
        {
            Width = size.Item1; Height = size.Item2; UpdateLayout(); ArrangeCards(); UpdateLayout();
            bool within = _cardGrid.Children.Cast<FrameworkElement>().All(card => {
                var bounds = card.TransformToAncestor(_cardGrid).TransformBounds(new Rect(card.RenderSize));
                return card.ActualWidth >= 275 && bounds.Left >= -1 && bounds.Right <= _scroll.ViewportWidth + 1;
            });
            bool buttonsFit = _buttons.All(b => b.DesiredSize.Width <= b.ActualWidth + b.Margin.Left + b.Margin.Right + 1);
            bool images = Descendants(_cardGrid).OfType<Image>().Count(x => x.Source?.Width > 0) == Role.Installed.Count;
            _scroll.ScrollToBottom(); UpdateLayout();
            var last = (FrameworkElement)_cardGrid.Children[^1];
            var bottom = last.TransformToAncestor(_scroll).TransformBounds(new Rect(last.RenderSize)).Bottom;
            bool reachable = bottom <= _scroll.ActualHeight + 1;
            bool passed = within && buttonsFit && images && reachable;
            results.Add(new { Width = ActualWidth, Height = ActualHeight, Columns = _cardGrid.ColumnDefinitions.Count,
                Cards = _cardGrid.Children.Count, within, buttonsFit, images, reachable, Passed = passed });
            SaveImage((FrameworkElement)Content, Path.Combine(directory, $"console-{size.Item1:0}-bottom.png"));
            _scroll.ScrollToTop(); UpdateLayout();
            SaveImage((FrameworkElement)Content, Path.Combine(directory, $"console-{size.Item1:0}-top.png"));
            if (size.Item1 == 1100) SaveImage(_cardGrid, Path.Combine(directory, "all-five-cards.png"));
            if (!passed) { File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(results)); throw new InvalidOperationException("Console layout verification failed."); }
        }
        File.WriteAllText(Path.Combine(directory, "layout.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static void SaveImage(FrameworkElement surface, string path)
    {
        var bounds = new Rect(0, 0, Math.Ceiling(surface.ActualWidth), Math.Ceiling(surface.ActualHeight));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) {
            drawing.DrawRectangle(Brush("#F6F5F1"), null, bounds);
            drawing.DrawRectangle(new VisualBrush(surface) { Stretch = Stretch.Fill }, null, bounds);
        }
        var bitmap = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private static TextBlock Text(string text, double size, string color) => new() { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
}
