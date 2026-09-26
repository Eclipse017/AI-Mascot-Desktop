using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using AIMascot.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AIMascot.Windows;

internal sealed class MascotApp : Application
{
    private static readonly string[] Commands = { "restore", "hide", "auto", "demo", "controls", "greet", "pet", "rest", "pause", "quit" };
    private readonly Dictionary<string, EventWaitHandle> _signals;
    private readonly List<RegisteredWaitHandle> _waits = new();
    private readonly string[] _args;
    private readonly string? _verifyDirectory;
    private readonly bool _verifyPolicy;
    private readonly AppPresence _detector = new();
    private readonly PresenceTracker _presence = new();
    private readonly FullscreenTracker _fullscreenTracker = new();
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _statusItem, _autoItem, _demoItem;
    private System.Drawing.Icon? _icon;
    private DragonWindow? _pet;
    private ControlWindow? _controls;
    private PresenceSample _lastSample = new(PresenceKind.Unknown, 0, 0, 0);
    private bool _polling, _exiting;
    private bool _fullscreen, _suspended, _eventsAttached;
    private int _probeGeneration;
    private readonly StartupRegistration _startup = new(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "AIMascot.Dragon.exe"));
    private StartupState _startupState = new(false, false);
    private int _visibilityTransitions;
    private readonly AIMascot.Platform.RuntimeStatus _runtimeStatus;

    private MascotApp(Dictionary<string, EventWaitHandle> signals, string[] args)
    {
        _signals = signals; _args = args;
        _verifyPolicy = args.Contains("--verify-policy");
        var index = Array.IndexOf(args, args.Contains("--verify-click") ? "--verify-click" : _verifyPolicy ? "--verify-policy" : "--verify");
        if (index >= 0)
        {
            if (index + 1 >= args.Length) throw new ArgumentException("Verification needs an output directory.");
            _verifyDirectory = Path.GetFullPath(args[index + 1]);
        }
        _runtimeStatus = new("dragon", _verifyDirectory);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--enable-startup") || args.Contains("--disable-startup"))
        {
            bool enable = args.Contains("--enable-startup");
            var result = new StartupRegistration(Environment.ProcessPath!).SetEnabled(enable);
            Environment.ExitCode = result.Available && result.Enabled == enable ? 0 : 2;
            return;
        }
        // Finite verification runs are isolated from the user's live pet and preferences.
        var suffix = args.Contains("--verify") || args.Contains("--verify-policy") || args.Contains("--verify-click") ? "." + Guid.NewGuid().ToString("N") : "";
        var signals = Commands.ToDictionary(command => command,
            command => new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AIMascot.Desktop.Dragon." + command + suffix));
        using var mutex = new Mutex(true, "Local\\AIMascot.Desktop.Dragon.Instance" + suffix, out var first);
        try
        {
            if (!first)
            {
                var requested = Commands.Where(command => args.Contains("--" + command)).ToArray();
                if (requested.Length == 0 && !args.Contains("--background")) signals["restore"].Set();
                else foreach (var command in requested) signals[command].Set();
                return;
            }
            if (args.Contains("--quit")) return;
            Environment.ExitCode = new MascotApp(signals, args).Run();
        }
        finally
        {
            if (first) mutex.ReleaseMutex();
            foreach (var signal in signals.Values) signal.Dispose();
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _pet = new DragonWindow(_verifyDirectory, _args.Contains("--inspect"));
        MainWindow = _pet;
        _pet.Interaction += kind => { _runtimeStatus.Interaction(kind, _pet.Left, _pet.Top); Dispatcher.BeginInvoke(WriteRuntimeStatus); };
        _pet.HideRequested += () => SetHidden(true);
        _pet.ControlsRequested += OpenControls;
        _pet.OptionsChanged += ApplyVisibility;
        _startupState = _verifyDirectory == null ? _startup.Read() : new(false, false, "验证实例不会修改自动启动设置。");
        _controls = new ControlWindow(settings => _pet.UpdatePreferences(settings), () => _pet.Preferences,
            SetStartup, _pet.Greet, _pet.Pet, _pet.ToggleRest, _pet.ResetPosition, Shutdown);
        using var stream = GetResourceStream(new Uri("pack://application:,,,/AIMascot.Dragon;component/Assets/dragon.ico")).Stream;
        _icon = new System.Drawing.Icon(stream);
        var menu = new Forms.ContextMenuStrip();
        _statusItem = new Forms.ToolStripMenuItem("正在确认应用状态…") { Enabled = false };
        menu.Items.Add(_statusItem);
        menu.Items.Add("龙娘控制台", null, (_, _) => Dispatcher.Invoke(OpenControls));
        _autoItem = new Forms.ToolStripMenuItem("跟随 Codex 应用", null, (_, _) => Dispatcher.Invoke(() => SetMode(DisplayMode.Auto)));
        _demoItem = new Forms.ToolStripMenuItem("本地演示", null, (_, _) => Dispatcher.Invoke(() => SetMode(DisplayMode.Demo)));
        menu.Items.Add(_autoItem); menu.Items.Add(_demoItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("恢复显示", null, (_, _) => Dispatcher.Invoke(() => SetHidden(false)));
        menu.Items.Add("手动隐藏", null, (_, _) => Dispatcher.Invoke(() => SetHidden(true)));
        menu.Items.Add("暂停 / 恢复动画", null, (_, _) => Dispatcher.Invoke(_pet.TogglePause));
        menu.Items.Add("摸摸头", null, (_, _) => Dispatcher.Invoke(_pet.Pet));
        menu.Items.Add("重置位置", null, (_, _) => Dispatcher.Invoke(_pet.ResetPosition));
        menu.Items.Add("退出龙娘", null, (_, _) => Dispatcher.Invoke(Shutdown));
        _tray = new Forms.NotifyIcon { Icon = _icon, Text = "ChatGPT 龙娘 · 本地陪伴", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => SetHidden(false));
        _pet.SettingsWriteFailed += () => _tray.ShowBalloonTip(2500, "龙娘", "暂时无法保存设置；本次仍可继续使用。", Forms.ToolTipIcon.Info);
        foreach (var (command, signal) in _signals)
            _waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
            {
                if (!_exiting && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => Execute(command));
            }, null, Timeout.Infinite, false));

        if (_verifyDirectory != null)
        {
            if (_args.Contains("--verify-click"))
            {
                _pet.UpdatePreferences(_pet.Preferences with { Mode = DisplayMode.Demo, UserHidden = false, Paused = false });
                _pet.StartClickVerification(_verifyDirectory, Shutdown);
            }
            else if (_verifyPolicy) StartPolicyVerification(_verifyDirectory);
            else
            {
                var before = Native.GetForegroundWindow();
                _pet.UpdatePreferences(_pet.Preferences with { Mode = DisplayMode.Demo, UserHidden = false, Paused = false });
                _pet.StartVerification(_verifyDirectory, Shutdown, before == Native.GetForegroundWindow());
            }
            return;
        }
        if (_args.Contains("--demo")) SetMode(DisplayMode.Demo);
        if (_args.Contains("--auto")) SetMode(DisplayMode.Auto);
        if (_args.Contains("--hide")) SetHidden(true);
        if (_args.Contains("--restore")) SetHidden(false);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        _eventsAttached = true;
        ApplyVisibility();
        _pollTimer.Tick += async (_, _) => await PollOnce();
        _pollTimer.Start();
        _ = PollOnce();
        if (_args.Contains("--controls")) OpenControls();
    }

    private async Task PollOnce()
    {
        if (_polling || _exiting) return;
        _polling = true;
        int generation = _probeGeneration;
        try
        {
            var result = await Task.Run(_detector.Probe);
            if (!_exiting && !_suspended && generation == _probeGeneration) AcceptSample(result.Sample, _uptime.Elapsed);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            if (!_exiting && !_suspended && generation == _probeGeneration) AcceptSample(new(PresenceKind.Unknown, 0, 0, 0), _uptime.Elapsed);
        }
        finally { _polling = false; }
    }
    private void AcceptSample(PresenceSample sample, TimeSpan now)
    {
        if (_verifyDirectory == null) _fullscreen = _fullscreenTracker.Observe(_pet!.Preferences.HideOnFullscreen && Native.FullscreenOnPetMonitor(_pet), now);
        _lastSample = sample; _presence.Observe(sample.Kind, now); ApplyVisibility();
    }
    private void SetStartup(bool enabled)
    {
        if (_verifyDirectory != null) return;
        _startupState = _startup.SetEnabled(enabled); ApplyVisibility();
    }
    private void OnDisplayChanged(object? sender, EventArgs e) => Post(() => { _pet!.HandleDisplayChange(); ApplyVisibility(); });
    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e) => Post(() =>
    {
        if (e.Mode == PowerModes.Suspend) SetSuspended(true);
        else if (e.Mode == PowerModes.Resume) SetSuspended(false);
    });
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e) => Post(() =>
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.RemoteDisconnect) SetSuspended(true);
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.RemoteConnect) SetSuspended(false);
    });
    private void Post(Action action) { if (!_exiting && !Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action); }
    private void SetSuspended(bool suspended)
    {
        if (_exiting) return;
        _suspended = suspended; _probeGeneration++; _pet!.SetSuspended(suspended);
        if (suspended) _pollTimer.Stop();
        else
        {
            _presence.Reset(); _fullscreenTracker.Reset(); _lastSample = new(PresenceKind.Unknown, 0, 0, 0); _fullscreen = false;
            if (_verifyDirectory == null) { _pollTimer.Start(); _ = PollOnce(); }
        }
        ApplyVisibility();
    }
    private void SetMode(DisplayMode mode) => _pet!.UpdatePreferences(_pet.Preferences with { Mode = mode });
    private void SetHidden(bool hidden) => _pet!.UpdatePreferences(_pet.Preferences with { UserHidden = hidden });
    private void SetPaused(bool paused) => _pet!.UpdatePreferences(_pet.Preferences with { Paused = paused });
    private void ApplyVisibility()
    {
        if (_pet == null || _exiting) return;
        var preferences = _pet.Preferences;
        bool show = DisplayPolicy.ShouldShow(preferences.Mode, preferences.UserHidden, _presence.Stable,
            _fullscreen && preferences.HideOnFullscreen, _suspended);
        if (show != _pet.IsVisible)
        {
            _visibilityTransitions++;
            if (show) _pet.Reveal(); else _pet.Conceal();
        }
        WriteRuntimeStatus();
        _controls?.Refresh(preferences, _lastSample, _presence.Stable, _pet.IsVisible, _startupState,
            _fullscreen && preferences.HideOnFullscreen, _suspended, _pet.Mood);
        if (_autoItem != null) _autoItem.Checked = preferences.Mode == DisplayMode.Auto;
        if (_demoItem != null) _demoItem.Checked = preferences.Mode == DisplayMode.Demo;
        if (_statusItem != null) _statusItem.Text = preferences.UserHidden ? "已手动隐藏"
            : _suspended ? "系统休息中" : _fullscreen && preferences.HideOnFullscreen ? "全屏期间暂时收起"
            : preferences.Mode == DisplayMode.Demo ? "本地演示中"
            : _lastSample.Kind == PresenceKind.Unknown ? "暂时无法确认应用状态"
            : _presence.Stable == PresenceKind.Running ? "Codex 已运行 · 陪伴中" : "等待 Codex 启动";
    }
    private void WriteRuntimeStatus()
    {
        if (_pet == null) return;
        var p = _pet.Preferences;
        string reason = _exiting ? "Stopped" : p.UserHidden ? "ManualHidden" : _suspended ? "Suspended"
            : _fullscreen && p.HideOnFullscreen ? "Fullscreen" : p.Mode == DisplayMode.Demo ? "Demo"
            : _presence.Stable == PresenceKind.Running ? "Following" : _lastSample.Kind == PresenceKind.Unknown ? "Unknown" : "WaitingForApp";
        _runtimeStatus.Write(new { Reason = reason, WpfVisible = _pet.IsVisible, AnimationRunning = _pet.AnimationRunning,
            Suspended = _suspended, Fullscreen = _fullscreen, Presence = _lastSample, Stable = _presence.Stable,
            NativeBodyHit = _pet.IsVisible ? Native.HitProbe(_pet, new Point(_pet.Width * .5, _pet.Height * .7)) : null,
            Mood = _pet.Mood.ToString(), Face = _pet.Face.ToString(), Left = double.IsFinite(_pet.Left) ? (double?)_pet.Left : null, Top = double.IsFinite(_pet.Top) ? (double?)_pet.Top : null });
    }
    private void OpenControls()
    {
        if (_verifyDirectory == null) _startupState = _startup.Read();
        ApplyVisibility(); _controls!.Show();
        if (_controls.WindowState == WindowState.Minimized) _controls.WindowState = WindowState.Normal;
        _controls.Activate();
    }
    private void Execute(string command)
    {
        if (_exiting) return;
        switch (command)
        {
            case "quit": Shutdown(); break;
            case "controls": OpenControls(); break;
            case "auto": SetMode(DisplayMode.Auto); break;
            case "demo": SetMode(DisplayMode.Demo); break;
            case "hide": SetHidden(true); break;
            case "greet": _pet!.Greet(); break;
            case "pet": _pet!.Pet(); break;
            case "rest": _pet!.ToggleRest(); break;
            case "pause": _pet!.TogglePause(); break;
            default: SetHidden(false); break;
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _exiting = true; _pollTimer.Stop();
        if (_eventsAttached)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            SystemEvents.PowerModeChanged -= OnPowerChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
        foreach (var wait in _waits) wait.Unregister(null);
        if (_controls != null) _controls.Exiting = true;
        _pet?.SaveAndStop();
        WriteRuntimeStatus();
        if (_tray != null) { _tray.Visible = false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _icon?.Dispose(); base.OnExit(e);
    }

    private void StartPolicyVerification(string directory)
    {
        Directory.CreateDirectory(directory);
        _pet!.UpdatePreferences(_pet.Preferences with { Mode = DisplayMode.Auto, UserHidden = false, Paused = false });
        var steps = new (string Name, Action Act, bool Visible)[] {
            ("Auto starts hidden", () => { }, false),
            ("One running sample waits", () => Feed(PresenceKind.Running, 0), false),
            ("Two running samples show", () => Feed(PresenceKind.Running, 1), true),
            ("Fourteen processes share one pet", () => Feed(PresenceKind.Running, 2, 14), true),
            ("Manual hide wins", () => { _pet.Left = 122; _pet.Top = 144; SetHidden(true); }, false),
            ("Polling cannot undo manual hide", () => Feed(PresenceKind.Running, 3), false),
            ("Manual restore resumes auto", () => SetHidden(false), true),
            ("Unknown holds during grace", () => Feed(PresenceKind.Unknown, 4), true),
            ("Unknown expires after five seconds", () => Feed(PresenceKind.Unknown, 9), false),
            ("Recovery requires confirmation", () => Feed(PresenceKind.Running, 10), false),
            ("Confirmed recovery shows", () => Feed(PresenceKind.Running, 11), true),
            ("Single missing sample tolerated", () => Feed(PresenceKind.Stopped, 12), true),
            ("Confirmed exit hides", () => Feed(PresenceKind.Stopped, 13), false),
            ("Demo shows without client", () => SetMode(DisplayMode.Demo), true),
            ("Manual hide also wins in demo", () => SetHidden(true), false),
            ("Restore demo", () => SetHidden(false), true),
            ("Pause stops only animation", () => SetPaused(true), true),
            ("Return to auto while absent", () => SetMode(DisplayMode.Auto), false),
            ("Reopen client", () => { Feed(PresenceKind.Running, 14); Feed(PresenceKind.Running, 15); }, true),
            ("Fullscreen hides", () => { _fullscreen = true; ApplyVisibility(); }, false),
            ("Hide during fullscreen", () => SetHidden(true), false),
            ("Leaving fullscreen respects manual hide", () => { _fullscreen = false; ApplyVisibility(); }, false),
            ("Restore after fullscreen", () => SetHidden(false), true),
            ("Suspend hides and stops animation", () => SetSuspended(true), false),
            ("Resume awaits fresh identity", () => SetSuspended(false), false),
            ("Resume first sample waits", () => Feed(PresenceKind.Running, 16), false),
            ("Resume second sample shows", () => Feed(PresenceKind.Running, 17), true)
        };
        var checks = new List<object>(); var failures = new List<string>(); int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            if (step == steps.Length)
            {
                timer.Stop();
                int petWindows = Windows.OfType<DragonWindow>().Count();
                if (petWindows != 1) failures.Add("Exactly one pet window expected");
                File.WriteAllText(Path.Combine(directory, "policy-lifecycle.json"), JsonSerializer.Serialize(new {
                    Scope = "Synthetic presence input through real WPF host; no real client closed.",
                    Passed = failures.Count == 0, Checks = checks, Failures = failures,
                    VisibilityTransitions = _visibilityTransitions, PetWindows = petWindows
                }, new JsonSerializerOptions { WriteIndented = true }));
                SetPaused(false);
                Feed(PresenceKind.Running, 18, 14); Feed(PresenceKind.Running, 19, 14);
                _controls!.SaveRender(Path.Combine(directory, "rendered-controls.png"));
                Shutdown(failures.Count == 0 ? 0 : 1); return;
            }
            var item = steps[step++]; var foreground = Native.GetForegroundWindow(); item.Act();
            bool visibleOk = _pet.IsVisible == item.Visible;
            bool timerOk = _pet.AnimationRunning == (item.Visible && !_pet.Preferences.Paused);
            bool focusOk = foreground == Native.GetForegroundWindow();
            // Native placement rounds to physical pixels at fractional display scales.
            bool positionOk = step < 5 || (Math.Abs(_pet.Left - 122) <= 1 && Math.Abs(_pet.Top - 144) <= 1);
            bool passed = visibleOk && timerOk && focusOk && positionOk;
            if (!passed) failures.Add(item.Name);
            checks.Add(new { item.Name, Passed = passed, Visible = _pet.IsVisible, Animation = _pet.AnimationRunning, ForegroundPreserved = focusOk, PositionPreserved = positionOk,
                Left = double.IsFinite(_pet.Left) ? (double?)_pet.Left : null, Top = double.IsFinite(_pet.Top) ? (double?)_pet.Top : null });
        };
        timer.Start();
        void Feed(PresenceKind kind, int seconds, int count = 1) =>
            AcceptSample(new(kind, kind == PresenceKind.Running ? count : 0, kind == PresenceKind.Unknown ? 1 : 0, count), TimeSpan.FromSeconds(seconds));
    }
}
