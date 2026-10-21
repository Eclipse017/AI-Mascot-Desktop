using System;
using System.Diagnostics;
using System.Windows;

namespace GeminiLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                var service = new LaunchService(AppDomain.CurrentDomain.BaseDirectory, info => { using (var p = Process.Start(info)) { } });
                new Application().Run(new PickerWindow(service.Launch));
            }
            catch (Exception exception)
            {
                MessageBox.Show("启动选择器无法打开。\n\n" + exception.Message, "Gemini", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
