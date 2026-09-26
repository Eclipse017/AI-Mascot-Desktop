using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIMascot.Core;

namespace AIMascot.Claude;

internal sealed class ControlWindow : Window
{
    private readonly TextBlock _status = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detail = new() { FontSize = 13, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _startupNote = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.SlateGray, Margin = new Thickness(22, 0, 0, 5) };
    private readonly RadioButton _auto = new() { Content = "跟随 Claude 应用", GroupName = "mode", Margin = new Thickness(0, 6, 0, 6) };
    private readonly RadioButton _demo = new() { Content = "本地演示", GroupName = "mode", Margin = new Thickness(18, 6, 0, 6) };
    private readonly CheckBox _hidden = Option("手动隐藏 Claude 娘");
    private readonly CheckBox _paused = Option("暂停动画");
    private readonly CheckBox _startup = Option("登录 Windows 后后台待命");
    private readonly CheckBox _fullscreen = Option("同屏全屏应用运行时收起");
    private readonly CheckBox _rest = Option("闲置 2 分钟后小憩");
    private readonly CheckBox _reduced = Option("减少晃动和特效");
    private readonly ComboBox _size = new() { Width = 155, Margin = new Thickness(12, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };
    private bool _updating;
    public bool Exiting { get; set; }

    public ControlWindow(Action<PetPreferences> apply, Func<PetPreferences> current, Action<bool> changeStartup,
        Action greet, Action pet, Action rest, Action reset, Action exit)
    {
        Title = "Claude 娘控制台"; Width = 460; Height = 720; MinWidth = 415; MinHeight = 520;
        MaxHeight = Math.Max(520, SystemParameters.WorkArea.Height - 32);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(253, 248, 243));
        Foreground = new SolidColorBrush(Color.FromRgb(69, 48, 38));
        var panel = new StackPanel { Margin = new Thickness(26, 24, 26, 24) };
        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var portrait = new Image { Source = new BitmapImage(new Uri("pack://application:,,,/AIMascot.Claude;component/Assets/idle.png")), Width = 72, Height = 72, Margin = new Thickness(0, 0, 16, 0) };
        RenderOptions.SetBitmapScalingMode(portrait, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(portrait, Dock.Left); title.Children.Add(portrait);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = "Claude 娘", FontSize = 27, FontWeight = FontWeights.SemiBold });
        titles.Children.Add(new TextBlock { Text = "把日常，陪成温柔的小事。", Foreground = Brushes.SlateGray, Margin = new Thickness(0, 5, 0, 0) });
        title.Children.Add(titles); panel.Children.Add(title);
        var statePanel = new StackPanel(); statePanel.Children.Add(_status); statePanel.Children.Add(_detail);
        panel.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Child = statePanel, Margin = new Thickness(0, 0, 0, 18) });
        var modes = new StackPanel { Orientation = Orientation.Horizontal }; modes.Children.Add(_auto); modes.Children.Add(_demo); panel.Children.Add(modes);
        var interaction = new WrapPanel { Margin = new Thickness(0, 8, 0, 12) };
        interaction.Children.Add(Button("打招呼", greet)); interaction.Children.Add(Button("摸摸头", pet)); interaction.Children.Add(Button("唤醒 / 小憩", rest)); panel.Children.Add(interaction);
        panel.Children.Add(_hidden); panel.Children.Add(_paused);
        var sizes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 14) };
        sizes.Children.Add(new TextBlock { Text = "角色大小", VerticalAlignment = VerticalAlignment.Center });
        foreach (var (text, value) in new[] { ("小号 · 160",160), ("标准 · 220",220), ("大号 · 280",280), ("特大 · 360",360) }) _size.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        sizes.Children.Add(_size); panel.Children.Add(sizes);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(_startup); panel.Children.Add(_startupNote); panel.Children.Add(_fullscreen); panel.Children.Add(_rest); panel.Children.Add(_reduced);
        var bottom = new WrapPanel { Margin = new Thickness(0, 18, 0, 6) }; bottom.Children.Add(Button("重置位置", reset)); bottom.Children.Add(Button("退出 Claude 娘", exit)); panel.Children.Add(bottom);
        panel.Children.Add(new TextBlock { Text = "轻抚额头，按住左键拖动。纯本地陪伴，不会打开或调用 Claude。跟随模式只被动识别已运行的客户端；无需登录。关闭此面板仍继续陪伴。", FontSize = 12, Foreground = Brushes.SlateGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        Content = new ScrollViewer { Content = panel, Background = Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _auto.Checked += (_, _) => { if (!_updating) apply(current() with { Mode = DisplayMode.Auto }); };
        _demo.Checked += (_, _) => { if (!_updating) apply(current() with { Mode = DisplayMode.Demo }); };
        Bind(_hidden, value => apply(current() with { UserHidden = value }));
        Bind(_paused, value => apply(current() with { Paused = value }));
        Bind(_fullscreen, value => apply(current() with { HideOnFullscreen = value }));
        Bind(_rest, value => apply(current() with { RestEnabled = value }));
        Bind(_reduced, value => apply(current() with { ReducedMotion = value }));
        Bind(_startup, changeStartup);
        _size.SelectionChanged += (_, _) => { if (!_updating && _size.SelectedItem is ComboBoxItem { Tag: int size }) apply(current() with { Size = size }); };
        Closing += (_, e) => { if (!Exiting) { e.Cancel = true; Hide(); } };
    }
    private static CheckBox Option(string content) => new() { Content = content, Margin = new Thickness(0, 5, 0, 5) };
    private static Button Button(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 8, 0), MinHeight = 33 };
        button.Click += (_, _) => action(); return button;
    }
    private void Bind(CheckBox checkbox, Action<bool> action)
    {
        checkbox.Checked += (_, _) => { if (!_updating) action(true); };
        checkbox.Unchecked += (_, _) => { if (!_updating) action(false); };
    }
    public void Refresh(PetPreferences preferences, PresenceSample sample, PresenceKind stable, bool petVisible,
        StartupState? startup = null, bool fullscreen = false, bool suspended = false, PetMood mood = PetMood.Idle)
    {
        _updating = true;
        _auto.IsChecked = preferences.Mode == DisplayMode.Auto; _demo.IsChecked = preferences.Mode == DisplayMode.Demo;
        _hidden.IsChecked = preferences.UserHidden; _paused.IsChecked = preferences.Paused;
        _fullscreen.IsChecked = preferences.HideOnFullscreen; _rest.IsChecked = preferences.RestEnabled; _reduced.IsChecked = preferences.ReducedMotion;
        _rest.Content = $"闲置 {preferences.RestAfterSeconds / 60.0:0.#} 分钟后小憩";
        _startup.IsChecked = startup?.Enabled == true; _startup.IsEnabled = startup?.Available == true;
        _startupNote.Text = startup?.Message ?? (startup?.Enabled == true ? "已启用。登录后自动待命，Claude 运行时出现。" : "未启用时，需要手动打开 Claude 娘程序。");
        ComboBoxItem? selected = null;
        foreach (ComboBoxItem item in _size.Items) if (item.Tag is int size && Math.Abs(size - preferences.Size) < 1) selected = item;
        if (!ReferenceEquals(_size.SelectedItem, selected)) _size.SelectedItem = selected;
        _status.Text = preferences.UserHidden ? "Claude 娘已手动隐藏" : suspended ? "系统休息中" : fullscreen ? "全屏期间暂时收起"
            : preferences.Mode == DisplayMode.Demo ? "本地演示模式" : sample.Kind == PresenceKind.Unknown ? "正在确认应用状态…"
            : stable == PresenceKind.Running ? "Claude 已运行 · Claude 娘陪伴中" : "Claude 未运行 · 可切换本地演示";
        string moodText = preferences.Paused ? "动画已暂停" : mood switch { PetMood.Resting => "正在小憩，点击即可唤醒", PetMood.Happy => "摸摸头，好开心", PetMood.Dragging => "正在搬家", _ => "安静陪伴中" };
        _detail.Text = petVisible ? moodText + "。" : "Claude 娘已收起。";
        if (sample.MatchedProcesses > 0) _detail.Text += $"已合并 {sample.MatchedProcesses} 个应用进程。";
        if (preferences.UserHidden) _detail.Text += "取消手动隐藏后按所选模式恢复。";
        else if (fullscreen) _detail.Text += "离开全屏后自动恢复。";
        else if (preferences.Mode == DisplayMode.Auto && sample.Kind == PresenceKind.Unknown) _detail.Text += "暂时无法确认 Claude 安装或进程；超过 5 秒自动收起，也可使用本地演示。";
        _updating = false;
    }
    internal void SaveRender(string path)
    {
        Show(); UpdateLayout(); var surface = (FrameworkElement)Content; var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth*dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight*dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
