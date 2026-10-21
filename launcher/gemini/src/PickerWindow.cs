using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GeminiLauncher
{
    internal sealed class PickerWindow : Window
    {
        internal readonly Button GeminiButton, AntigravityButton;
        internal readonly TextBlock ErrorText;
        private readonly Action<string> launch;
        private bool busy;

        internal PickerWindow(Action<string> launch)
        {
            this.launch = launch;
            Title = "Gemini · 选择启动";
            Width = 520;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Brush("#F8F7FC");
            FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true;
            Icon = ResourceImage("Gemini.ico");
            var body = new StackPanel { Margin = new Thickness(28, 22, 28, 24) };
            var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            header.ColumnDefinitions.Add(new ColumnDefinition());
            var portrait = new Image { Source = ResourceImage("Gemini.png"), Width = 100, Height = 100, Stretch = Stretch.Uniform };
            header.Children.Add(portrait);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            words.Children.Add(Text("Gemini", 27, "#33264E", true));
            words.Children.Add(Text("今天想打开哪个？", 15, "#746785", false));
            Grid.SetColumn(words, 1);
            header.Children.Add(words);
            body.Children.Add(header);
            GeminiButton = Choice("Gemini 桌面版", "聊天、提问与灵感", "G", "#7753BD", "gemini");
            AntigravityButton = Choice("Antigravity", "Agent · 编程与项目开发", "A", "#3C688C", "antigravity");
            body.Children.Add(GeminiButton);
            body.Children.Add(AntigravityButton);
            ErrorText = Text("", 12, "#A22D45", false);
            ErrorText.TextWrapping = TextWrapping.Wrap;
            ErrorText.Margin = new Thickness(2, 6, 2, 8);
            ErrorText.Visibility = Visibility.Collapsed;
            AutomationProperties.SetLiveSetting(ErrorText, AutomationLiveSetting.Assertive);
            body.Children.Add(ErrorText);
            body.Children.Add(Text("选择后打开应用  ·  Esc 关闭", 12, "#82768D", false));
            Content = body;
            PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        private Button Choice(string title, string subtitle, string letter, string accent, string id)
        {
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            panel.ColumnDefinitions.Add(new ColumnDefinition());
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            var badge = new Border { Width = 38, Height = 38, Background = Brush(accent), CornerRadius = new CornerRadius(12), Child = new TextBlock { Text = letter, FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            panel.Children.Add(badge);
            var labels = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(Text(title, 18, "#33264E", true));
            labels.Children.Add(Text(subtitle, 12, "#82768D", false));
            Grid.SetColumn(labels, 1);
            panel.Children.Add(labels);
            var arrow = Text("›", 28, accent, false);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(arrow, 2);
            panel.Children.Add(arrow);
            var button = new Button { Content = panel, Background = Brushes.White, BorderBrush = Brush("#E1D9EF"), BorderThickness = new Thickness(1), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 12), HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand };
            AutomationProperties.SetName(button, title);
            AutomationProperties.SetAutomationId(button, id);
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#F0EAF9")));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Brush(accent)));
            focus.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2)));
            template.Triggers.Add(focus);
            button.Template = template;
            button.Click += (_, __) => Select(id);
            return button;
        }

        private void Select(string id)
        {
            if (busy) return;
            busy = true;
            GeminiButton.IsEnabled = AntigravityButton.IsEnabled = false;
            try { launch(id); Close(); }
            catch (Exception exception)
            {
                ErrorText.Text = "打开失败：" + exception.Message;
                ErrorText.Visibility = Visibility.Visible;
                GeminiButton.IsEnabled = AntigravityButton.IsEnabled = true;
                busy = false;
            }
        }

        private static TextBlock Text(string text, double size, string color, bool bold)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 0, 2) };
        }
        private static Brush Brush(string color) { return (Brush)new BrushConverter().ConvertFromString(color); }
        private static BitmapSource ResourceImage(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Assets." + name))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0]; frame.Freeze(); return frame;
            }
        }
    }
}
