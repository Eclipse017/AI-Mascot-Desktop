using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AIMascot.Core;

namespace AIMascot.DeepSeek;

internal sealed class DeepSeekWindow : Window
{
    private readonly CompanionBehavior _behavior = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly PreferencesStore _store;
    private PetPreferences _settings;
    private bool _positionInitialized;
    private readonly ScaleTransform _scale = new();
    private readonly RotateTransform _rotate = new();
    private readonly TranslateTransform _translate = new();
    private readonly SpriteImage _portrait;
    private readonly PetEffects _effects;
    private readonly Grid _character;
    private Native.POINT _pressed;
    private double _startLeft, _startTop;
    private bool _pressedInside, _dragged;
    private bool _platformSuspended;
    private CompanionFrame _frame;
    public event Action<string>? Interaction;
    internal PetFace Face => _frame.Face;
    public event Action? SettingsWriteFailed;
    public event Action? HideRequested;
    public event Action? ControlsRequested;
    public event Action? OptionsChanged;
    internal bool AnimationRunning => _timer.IsEnabled;
    internal bool AssetsFallback => _portrait.UsesFallback;
    internal PetMood Mood => _frame.Mood;
    public PetPreferences Preferences => _settings with
    {
        Left = double.IsFinite(Left) ? Left : _settings.Left,
        Top = double.IsFinite(Top) ? Top : _settings.Top,
        Size = Width - 40,
        Paused = _behavior.Paused
    };

    public DeepSeekWindow(string? verificationDirectory, bool inspect)
    {
        _store = new(verificationDirectory); _settings = _store.Load();
        _behavior.Paused = _settings.Paused;
        _behavior.RestEnabled = _settings.RestEnabled; _behavior.RestAfterSeconds = _settings.RestAfterSeconds;
        Title = "DeepSeek 娘 · 本地陪伴";
        Width = Height = _settings.Size + 40;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true;
        ShowInTaskbar = inspect; ShowActivated = false; Focusable = false; UseLayoutRounding = true;
        _portrait = new SpriteImage { Cursor = Cursors.Hand,
            ToolTip = "点击打招呼 · 拖动搬家 · 在额头来回移动可摸头 · 右键菜单" };
        _effects = new PetEffects();
        _character = new Grid { Margin = new Thickness(20), RenderTransformOrigin = new Point(.5, .72) };
        _character.Children.Add(_portrait); _character.Children.Add(_effects);
        var transforms = new TransformGroup(); transforms.Children.Add(_scale); transforms.Children.Add(_rotate); transforms.Children.Add(_translate);
        _character.RenderTransform = transforms; Content = _character;
        SourceInitialized += (_, _) => { Native.Configure(this, inspect, HandleDisplayChange); InitializePosition(); };
        Loaded += (_, _) => { InitializePosition(); UpdateTimer(); };
        IsVisibleChanged += (_, _) => UpdateTimer();
        _timer.Tick += (_, _) => ApplyPose();
        _portrait.MouseLeftButtonDown += OnPress; _portrait.MouseMove += OnMove; _portrait.MouseLeftButtonUp += OnRelease;
        _portrait.MouseLeave += (_, _) => _behavior.ClearHover();
        _portrait.LostMouseCapture += (_, _) => { EndGesture(); Interaction?.Invoke("capture-lost"); };
        _portrait.MouseRightButtonUp += (_, args) => { OpenMenu(); args.Handled = true; };
        Closed += (_, _) => { SaveAndStop(); Application.Current.Shutdown(); };
    }
    private void InitializePosition()
    {
        if (_positionInitialized) return; _positionInitialized = true;
        if (_settings.Left is double x && _settings.Top is double y) { Left = x; Top = y; Native.ClampToScreen(this); }
        else ResetPosition();
    }
    private void ApplyPose(double? at = null)
    {
        _frame = _behavior.Sample(at ?? _clock.Elapsed.TotalSeconds);
        var pose = _settings.ReducedMotion ? new Pose(1, 1, 0, 0) : _frame.Pose;
        _scale.ScaleX = pose.ScaleX; _scale.ScaleY = pose.ScaleY; _rotate.Angle = pose.Angle; _translate.Y = pose.OffsetY;
        _portrait.SetFace(_frame.Face);
        _effects.Frame = _settings.ReducedMotion ? _frame with { Sparkle = 0 } : _frame; _effects.InvalidateVisual();
        _timer.Interval = TimeSpan.FromMilliseconds(_settings.ReducedMotion ? 100 : _frame.Mood == PetMood.Resting ? 80 : 33);
    }
    private void UpdateTimer()
    {
        if (IsVisible && !_behavior.Paused && !_platformSuspended) _timer.Start(); else _timer.Stop();
        ApplyPose();
    }
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (!Native.GetCursorPos(out _pressed)) return;
        Native.RaiseWithoutActivation(this);
        _startLeft = Left; _startTop = Top; _pressedInside = true; _dragged = false;
        _portrait.CaptureMouse(); Interaction?.Invoke("press"); e.Handled = true;
    }
    private void OnMove(object sender, MouseEventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (!_pressedInside || e.LeftButton != MouseButtonState.Pressed)
        {
            var relative = e.GetPosition(_portrait);
            if (_portrait.Hit(relative)) DeepSeekInteraction.Hover(_behavior, relative.X / _portrait.ActualWidth, relative.Y / _portrait.ActualHeight, now);
            return;
        }
        if (!Native.GetCursorPos(out var current)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double dx = (current.X - _pressed.X) / dpi.DpiScaleX, dy = (current.Y - _pressed.Y) / dpi.DpiScaleY;
        AdvanceDrag(dx, dy, now); e.Handled = true;
    }
    private void AdvanceDrag(double dx, double dy, double now)
    {
        if (!_dragged && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance) return;
        if (!_dragged) { _dragged = true; _behavior.BeginDrag(now); }
        Left = _startLeft + dx; Top = _startTop + dy; ApplyPose();
    }
    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!_pressedInside) return;
        bool wasDrag = _dragged; EndGesture(); Interaction?.Invoke(wasDrag ? "release-drag" : "release-click");
        if (wasDrag) { Native.ClampToScreen(this); Save(); } else Greet();
        ApplyPose(); e.Handled = true;
    }
    private void EndGesture()
    {
        _pressedInside = false; _dragged = false; _behavior.EndDrag(_clock.Elapsed.TotalSeconds);
        if (_portrait.IsMouseCaptured) _portrait.ReleaseMouseCapture();
    }
    private void OpenMenu()
    {
        var menu = new ContextMenu();
        Add("打个招呼", Greet); Add("摸摸头", Pet); Add("唤醒 / 休息", ToggleRest);
        Add(_behavior.Paused ? "恢复动画" : "暂停动画", TogglePause); menu.Items.Add(new Separator());
        Add("小号", () => ResizePet(160)); Add("标准大小", () => ResizePet(220)); Add("大号", () => ResizePet(280));
        Add("重置位置", ResetPosition); Add("DeepSeek 娘控制台", () => ControlsRequested?.Invoke()); menu.Items.Add(new Separator());
        Add("隐藏（托盘双击可恢复）", () => HideRequested?.Invoke()); Add("退出DeepSeek 娘", () => Application.Current.Shutdown());
        menu.PlacementTarget = _portrait; menu.IsOpen = true;
        void Add(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    }
    public void Greet() { _behavior.Click(_clock.Elapsed.TotalSeconds); ApplyPose(); Interaction?.Invoke("greet"); }
    public void Pet() { _behavior.Pet(_clock.Elapsed.TotalSeconds); ApplyPose(); }
    public void ToggleRest()
    {
        if (_frame.Mood == PetMood.Resting) _behavior.Wake(_clock.Elapsed.TotalSeconds);
        else _behavior.Rest(_clock.Elapsed.TotalSeconds);
        ApplyPose();
    }
    public void ResizePet(double size)
    {
        Width = Height = Math.Clamp(size, 120, 360) + 40; Native.ClampToScreen(this); Save(); OptionsChanged?.Invoke();
    }
    public void ResetPosition()
    {
        var area = SystemParameters.WorkArea; int columns = Math.Max(1, (int)((area.Width - 32) / (Width + 12)));
        Left = area.Right - Width - 24 - (4 % columns) * (Width + 12); Top = area.Bottom - Height - 24 - (4 / columns) * (Height + 12);
        Native.ClampToScreen(this); Save();
    }
    public void HandleDisplayChange() { EndGesture(); Native.ClampToScreen(this); Save(); }
    public void SetSuspended(bool suspended)
    {
        _platformSuspended = suspended; EndGesture();
        if (!suspended) { _behavior.Wake(_clock.Elapsed.TotalSeconds); HandleDisplayChange(); }
        UpdateTimer();
    }
    public void Reveal() { if (!IsVisible) Show(); Native.ClampToScreen(this); UpdateTimer(); }
    public void Conceal() { if (!IsVisible) return; EndGesture(); _behavior.ClearHover(); Save(); Hide(); }
    public void TogglePause() => UpdatePreferences(Preferences with { Paused = !_behavior.Paused });
    public void UpdatePreferences(PetPreferences settings)
    {
        _settings = settings.Sanitize(); _behavior.Paused = _settings.Paused;
        _behavior.RestEnabled = _settings.RestEnabled; _behavior.RestAfterSeconds = _settings.RestAfterSeconds;
        Width = Height = _settings.Size + 40;
        if (IsVisible) { UpdateLayout(); Native.ClampToScreen(this); }
        UpdateTimer(); Save(); OptionsChanged?.Invoke();
    }
    private void Save() { _settings = Preferences.Sanitize(); if (!_store.Save(_settings)) SettingsWriteFailed?.Invoke(); }
    public void SaveAndStop() { EndGesture(); _timer.Stop(); Save(); }
    internal void SaveRender(string path)
    {
        UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Width * dpi.DpiScaleX), (int)Math.Ceiling(Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    // Exercise the real routed press/release handlers without injecting operating-system input.
    internal void StartClickVerification(string directory, Action<int> done)
    {
        Directory.CreateDirectory(directory);
        Reveal();
        var cover = new Window { Left = Left, Top = Top, Width = Width, Height = Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true,
            ShowActivated = false, ShowInTaskbar = false, Background = Brushes.SlateBlue };
        cover.Show();
        var samples = new System.Collections.Generic.List<object>();
        int clicks = 0, releases = 0, dragReleases = 0;
        bool focusPreserved = true, nativeBodyHit = true, lostCaptureSafe = false, dragSaved = false;
        Interaction += kind => { if (kind == "release-click") releases++; if (kind == "release-drag") dragReleases++; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            if (clicks == 8)
            {
                _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                _portrait.ReleaseMouseCapture();
                _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                lostCaptureSafe = !_portrait.IsMouseCaptured && releases == 8;
                clicks++; return;
            }
            if (clicks == 9)
            {
                double beforeX = Left;
                _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
                AdvanceDrag(-36, -16, _clock.Elapsed.TotalSeconds);
                _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
                dragSaved = releases == 8 && dragReleases == 1 && Math.Abs(Left - beforeX + 36) < 1 &&
                    _store.Load().Left == Left && !_portrait.IsMouseCaptured;
                clicks++; return;
            }
            if (clicks >= 10)
            {
                timer.Stop(); cover.Close();
                SaveRender(Path.Combine(directory, "after-clicks.png"));
                bool passed = IsVisible && AnimationRunning && releases == 8 && !_portrait.IsMouseCaptured &&
                    focusPreserved && nativeBodyHit && lostCaptureSafe && dragSaved;
                File.WriteAllText(Path.Combine(directory, "clicks.json"), JsonSerializer.Serialize(new {
                    Passed = passed, Releases = releases, Visible = IsVisible, AnimationRunning,
                    MouseCaptureReleased = !_portrait.IsMouseCaptured, ForegroundPreserved = focusPreserved,
                    NativeBodyHit = nativeBodyHit, LostCaptureSafe = lostCaptureSafe, DragSaved = dragSaved, Samples = samples,
                    Scope = "Synthetic routed WPF mouse events through real press/release handlers; overlapping topmost fixture; not OS mouse input."
                }, new JsonSerializerOptions { WriteIndented = true }));
                done(passed ? 0 : 1); return;
            }
            var foreground = Native.GetForegroundWindow();
            _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseDownEvent });
            _portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.MouseUpEvent });
            clicks++; ApplyPose(); UpdateLayout();
            focusPreserved &= foreground == Native.GetForegroundWindow();
            nativeBodyHit &= Native.HitProbe(this, new Point(Width * .5, Height * .7)).PetHit;
            samples.Add(new { Click = clicks, Visible = IsVisible, AnimationRunning,
                ForegroundPreserved = foreground == Native.GetForegroundWindow(), NativeBody = Native.HitProbe(this, new Point(Width * .5, Height * .7)) });
        };
        timer.Start();
    }
    // Finite internal tests: real window methods and native hit probing, no injected OS input.
    public void StartVerification(string directory, Action done, bool showPreservedForeground)
    {
        Directory.CreateDirectory(directory);
        var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        int step = 0; bool hiddenStops = false, resumed = false, pauseStops = false;
        check.Tick += (_, _) =>
        {
            switch (step++)
            {
                case 0:
                    SaveRender(Path.Combine(directory, "rendered-window.png")); Greet(); break;
                case 1: Conceal(); hiddenStops = !_timer.IsEnabled && !IsVisible; break;
                case 2: Reveal(); resumed = _timer.IsEnabled && IsVisible; break;
                case 3: TogglePause(); pauseStops = !_timer.IsEnabled; break;
                case 4:
                    TogglePause(); Pet(); SaveRender(Path.Combine(directory, "rendered-happy.png")); break;
                case 5:
                    _behavior.Rest(_clock.Elapsed.TotalSeconds); ApplyPose(); SaveRender(Path.Combine(directory, "rendered-rest.png")); break;
                default:
                    _behavior.Wake(_clock.Elapsed.TotalSeconds); ApplyPose(); Save();
                    File.WriteAllText(Path.Combine(directory, "lifecycle.json"), JsonSerializer.Serialize(new {
                        HiddenStopsTimer = hiddenStops, RevealRestartsTimer = resumed, PauseStopsTimer = pauseStops,
                        PreferencesRoundTrip = Math.Abs(_store.Load().Size - (Width - 40)) < .01,
                        ShowPreservedForeground = showPreservedForeground, AssetsFallback = AssetsFallback,
                        AlphaHitTransparent = !_portrait.Hit(new Point(0, 0)), AlphaHitBody = _portrait.Hit(new Point(_portrait.ActualWidth * .5, _portrait.ActualHeight * .7)),
                        NativeCorner = Native.HitProbe(this, new Point(2, 2)), NativeBody = Native.HitProbe(this, new Point(Width * .5, Height * .7)),
                        OsInputTests = "UNVERIFIED by this internal check"
                    }, new JsonSerializerOptions { WriteIndented = true }));
                    check.Stop(); done(); break;
            }
        };
        check.Start();
    }
}
