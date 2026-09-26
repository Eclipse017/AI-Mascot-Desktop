using System.IO;
using System.Text.Json;
using System.Windows;

namespace AIMascot.ConsoleApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var controller = new RoleController();
            if (args.Length == 2 && args[0] == "--status")
            { Write(args[1], Role.Installed.Select(controller.Read).ToArray()); return; }
            if (args.Length == 4 && args[0] == "--command")
            {
                var roles = args[1] == "all" ? Role.Installed : Role.Installed.Where(r => r.Id == args[1]).ToArray();
                if (roles.Count == 0) throw new ArgumentException("Unknown role.");
                var results = roles.Select(r => controller.ExecuteAsync(r, args[2]).GetAwaiter().GetResult()).ToArray();
                Write(args[3], results); Environment.ExitCode = results.All(r => r.Success) ? 0 : 2; return;
            }
            bool verify = args.Length == 2 && args[0] == "--verify";
            string suffix = verify ? "." + Guid.NewGuid().ToString("N") : "";
            using var mutex = new Mutex(true, "Local\\AIMascot.Desktop.Console.Instance" + suffix, out bool first);
            using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AIMascot.Desktop.Console.Show" + suffix);
            using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AIMascot.Desktop.Console.Quit" + suffix);
            if (!first) { if (args.Contains("--quit")) quit.Set(); else activate.Set(); return; }
            try
            {
                if (args.Contains("--quit")) return;
                var app = new Application(); var window = new MainWindow(startCompanions: !verify);
                var waiter = ThreadPool.RegisterWaitForSingleObject(activate, (_, _) => {
                    if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(() => { window.Show(); window.WindowState = WindowState.Normal; window.Activate(); });
                }, null, Timeout.Infinite, false);
                var exitWaiter = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => {
                    if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(window.Close);
                }, null, Timeout.Infinite, false);
                window.Closed += (_, _) => { waiter.Unregister(null); exitWaiter.Unregister(null); };
                if (verify) window.Loaded += async (_, _) => {
                    try {
                        await Task.Delay(1000); await window.Refresh(); window.VerifyLayout(args[1]);
                        Write(Path.Combine(args[1], "console-status.json"), Role.Installed.Select(controller.Read).ToArray());
                    } catch (Exception e) { Environment.ExitCode = 2; Write(Path.Combine(args[1], "error.json"), new { Error = e.Message }); }
                    finally { window.Close(); }
                };
                app.Run(window);
            }
            finally { mutex.ReleaseMutex(); }
        }
        catch (Exception e)
        {
            Environment.ExitCode = 2;
            if (args.Length == 0) MessageBox.Show(e.Message, "AI 娘控制台", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private static void Write(string path, object value)
    {
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    }
}
