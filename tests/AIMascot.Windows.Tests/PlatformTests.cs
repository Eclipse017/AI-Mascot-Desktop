using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AIMascot.Core;
using AIMascot.Windows;

internal static class PlatformTests
{
    public static int Run(string mode, string directory)
    {
        int result = 1;
        var thread = new Thread(() =>
        {
            try
            {
                Directory.CreateDirectory(directory);
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                if (mode == "--fullscreen-fixture") Fixture(app, directory);
                else Probe(app, directory);
                result = app.Run();
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(directory, "platform-error.txt"), error.ToString()); result = 1; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        return result;
    }
    private static void Fixture(Application app, string directory)
    {
        var window = new Window { Title = "龙娘全屏检测测试（自动关闭）", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Left = 0, Top = 0, Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight,
            Background = new SolidColorBrush(Color.FromRgb(34, 28, 48)), ShowInTaskbar = false, Topmost = true,
            Content = new TextBlock { Text = "龙娘窗口兼容性测试\n正在验证全屏与普通窗口识别，数秒后自动关闭。", Foreground = Brushes.White,
                FontSize = 24, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        window.Show(); window.Activate();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) }; int phase = 0;
        timer.Tick += (_, _) =>
        {
            if (phase == 0 && clock.Elapsed.TotalSeconds >= 1) { Write("full"); phase = 1; }
            else if (phase == 1 && clock.Elapsed.TotalSeconds >= 4)
            {
                var area = SystemParameters.WorkArea; window.Left = area.Left; window.Top = area.Top;
                window.Width = area.Width; window.Height = Math.Max(200, area.Height - 60); window.UpdateLayout(); phase = 2;
            }
            else if (phase == 2 && clock.Elapsed.TotalSeconds >= 5) { Write("work-area"); phase = 3; }
            else if (clock.Elapsed.TotalSeconds >= 8) { timer.Stop(); Write("closed"); app.Shutdown(); }
        };
        timer.Start();
        void Write(string phaseName)
        {
            string path = Path.Combine(directory, "fixture-state.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { Phase = phaseName, Active = Native.GetForegroundWindow() == new WindowInteropHelper(window).Handle }));
            File.Move(path + ".tmp", path, true);
        }
    }
    private static void Probe(Application app, string directory)
    {
        var pet = new DragonWindow(Path.Combine(directory, "settings"), inspect: false);
        pet.UpdatePreferences(pet.Preferences with { Mode = DisplayMode.Demo, Paused = true }); pet.Show();
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--fullscreen-fixture"); start.ArgumentList.Add(directory);
        using (var current = Process.GetCurrentProcess())
        {
            string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            string? dotnetRoot = Directory.GetParent(runtime)?.Parent?.Parent?.FullName;
            if (dotnetRoot != null && File.Exists(Path.Combine(dotnetRoot, "dotnet.exe"))) start.Environment["DOTNET_ROOT"] = dotnetRoot;
        }
        var child = Process.Start(start) ?? throw new InvalidOperationException("Fixture did not start");
        var samples = new List<object>(); bool full = false, ordinary = false; var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            var path = Path.Combine(directory, "fixture-state.json");
            if (File.Exists(path))
            {
                try
                {
                    using var json = JsonDocument.Parse(File.ReadAllText(path));
                    string phase = json.RootElement.GetProperty("Phase").GetString()!;
                    bool active = json.RootElement.GetProperty("Active").GetBoolean();
                    bool result = Native.FullscreenOnPetMonitor(pet);
                    samples.Add(new { Phase = phase, FixtureForeground = active, FullscreenDetected = result });
                    if (phase == "full" && active && result) full = true;
                    if (phase == "work-area" && active && !result) ordinary = true;
                }
                catch (IOException) { }
            }
            if (child.HasExited || clock.Elapsed.TotalSeconds > 15)
            {
                timer.Stop();
                bool childExited = child.HasExited;
                File.WriteAllText(Path.Combine(directory, "native-platform.json"), JsonSerializer.Serialize(new {
                    Passed = full && ordinary && childExited, FullscreenRecognized = full, OrdinaryWindowExcluded = ordinary,
                    FixtureExited = childExited, Scope = "Two local processes with real HWNDs; synthetic fullscreen fixture. No user application closed or OS input injected.", Samples = samples
                }, new JsonSerializerOptions { WriteIndented = true }));
                child.Dispose(); app.Shutdown(full && ordinary && childExited ? 0 : 1);
            }
        };
        timer.Start();
    }
}
