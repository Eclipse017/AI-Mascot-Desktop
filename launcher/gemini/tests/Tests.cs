using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace GeminiLauncher
{
    internal static class Tests
    {
        private static int checks;
        private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
        private static void Fails(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string evidence = args[0]; Directory.CreateDirectory(evidence);
                string root = Path.Combine(evidence, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "targets"));
                string exe = Path.Combine(root, "inert.exe"); File.WriteAllText(exe, "inert fixture, never executed");
                foreach (var id in new[] { "gemini", "antigravity" }) File.WriteAllText(Path.Combine(root, "targets", id + ".lnk"), "inert fixture");
                var document = new XmlDocument();
                var top = document.CreateElement("targets"); document.AppendChild(top);
                foreach (var id in new[] { "gemini", "antigravity" }) { var item = document.CreateElement("app"); item.SetAttribute("id", id); item.SetAttribute("shortcut", id + ".lnk"); item.SetAttribute("executable", exe); top.AppendChild(item); }
                string config = Path.Combine(root, "targets.xml"); document.Save(config);
                var calls = new List<ProcessStartInfo>();
                var service = new LaunchService(root, calls.Add);
                Check(calls.Count == 0, "configuration never starts an app");
                service.Launch("gemini"); service.Launch("antigravity");
                Check(calls.Count == 2 && calls[0].FileName.EndsWith("gemini.lnk") && calls[1].FileName.EndsWith("antigravity.lnk"), "each selection uses its own original shortcut");
                Check(calls[0].UseShellExecute && calls[0].Arguments == "", "Shell preserves original shortcut arguments");
                Fails(() => service.Launch("unknown"), "unknown choice rejected");
                File.Delete(exe); Fails(() => service.Launch("gemini"), "missing installation rejected");
                File.WriteAllText(exe, "inert"); File.Delete(Path.Combine(root, "targets", "gemini.lnk"));
                Fails(() => service.Launch("gemini"), "missing shortcut rejected");
                Check(calls.Count == 2, "failed launches do not start a process");
                ((XmlElement)top.FirstChild).SetAttribute("shortcut", "..\\Gemini.lnk"); document.Save(config);
                Fails(() => new LaunchService(root, calls.Add), "recursive or traversing shortcut rejected");
                ((XmlElement)top.FirstChild).SetAttribute("shortcut", "gemini.lnk"); ((XmlElement)top.LastChild).SetAttribute("id", "gemini"); document.Save(config);
                Fails(() => new LaunchService(root, calls.Add), "duplicate targets rejected");
                File.WriteAllText(config, "<!DOCTYPE targets [<!ENTITY e SYSTEM 'file:///never'>]><targets>&e;</targets>");
                Fails(() => new LaunchService(root, calls.Add), "external XML entities rejected");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                foreach (var id in new[] { "gemini", "antigravity" })
                {
                    var selected = new List<string>(); var window = new PickerWindow(selected.Add); bool closed = false; window.Closed += (_, __) => closed = true; window.Show();
                    Check(selected.Count == 0, "show picker without starting apps");
                    (id == "gemini" ? window.GeminiButton : window.AntigravityButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(selected.Count == 1 && selected[0] == id && closed, "button launches only selected app then closes");
                }
                int attempts = 0;
                var retry = new PickerWindow(id => { attempts++; if (attempts == 1) throw new IOException("测试：目标不可用"); }); retry.Show();
                retry.GeminiButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(retry.IsVisible && retry.ErrorText.Visibility == Visibility.Visible && retry.AntigravityButton.IsEnabled, "launch failure stays visible with retry");
                retry.AntigravityButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(attempts == 2 && !retry.IsVisible, "another option can succeed after failure");
                var preview = new PickerWindow(id => { throw new Exception("preview must never launch"); }); preview.Show(); preview.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth), (int)Math.Ceiling(preview.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(preview);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(evidence, "picker.png"))) encoder.Save(file);
                preview.Close(); app.Shutdown();
                Console.WriteLine("PASS: " + checks + " checks. Fixture launches only; no AI application started."); return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }
    }
}
