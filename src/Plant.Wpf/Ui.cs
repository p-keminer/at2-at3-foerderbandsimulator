using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Plant.Wpf
{
    internal static class Ui
    {
        public static readonly Brush Ink = Brush("#173047"), Background = Brush("#EDF2F6"), Surface = Brush("#FFFFFF"),
            Accent = Brush("#006F78"), Warning = Brush("#9A4C00"), Belt = Brush("#243C48");
        public static Brush Brush(string color) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; b.Freeze(); return b; }
        public static TextBlock Text(string text, double size = 14, bool bold = false) => new()
        { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Foreground = Ink,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 5) };
        public static Button Button(string label, Action action)
        {
            var b = new Button { Content = label, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 3, 6, 3), MinHeight = 34, Cursor = System.Windows.Input.Cursors.Hand };
            AutomationProperties.SetName(b, label); b.Click += (_, _) => action(); return b;
        }
        public static CheckBox Check(string label, bool initial = false)
        {
            var b = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = initial, Margin = new Thickness(0, 5, 0, 5), VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(b, label); return b;
        }
        public static TextBox Number(Panel parent, string label, double value)
        {
            var input = new TextBox { Text = value.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(0, 0, 0, 7), Padding = new Thickness(6), MinHeight = 30 };
            parent.Children.Add(new Label { Content = label, Target = input, Padding = new Thickness(0, 4, 0, 3) });
            parent.Children.Add(input); AutomationProperties.SetName(input, label); return input;
        }
        public static double Value(TextBox b)
        {
            if (!double.TryParse(b.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var v) || !Core.Numbers.Finite(v))
            { b.Focus(); throw new ArgumentException(AutomationProperties.GetName(b) + ": endliche Zahl eingeben (Dezimalkomma). "); }
            return v;
        }
        public static ComboBox Select(Panel parent, string label, params string[] options)
        {
            var c = new ComboBox { ItemsSource = options, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4), MinHeight = 30 };
            parent.Children.Add(new Label { Content = label, Target = c, Padding = new Thickness(0, 4, 0, 3) }); parent.Children.Add(c);
            AutomationProperties.SetName(c, label); return c;
        }
        public static StackPanel Section(Panel parent, string label)
        {
            var inner = new StackPanel(); inner.Children.Add(Text(label, 16, true));
            parent.Children.Add(new Border { Background = Surface, CornerRadius = new CornerRadius(6), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), Child = inner });
            return inner;
        }
        public static WrapPanel Row(Panel parent) { var p = new WrapPanel(); parent.Children.Add(p); return p; }
        public static StackPanel Fold(Panel parent, string label)
        {
            var content = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var fold = new Expander { Header = label, Content = content, Margin = new Thickness(0, 8, 0, 4), HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(fold, label); parent.Children.Add(fold); return content;
        }
        public static ScrollViewer Scroll(UIElement child) => new() { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(8) };
    }
}
