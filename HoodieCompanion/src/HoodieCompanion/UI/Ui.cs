using System;
using System.IO;
using Path = System.Windows.Shapes.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HoodieCompanion.UI;

/// <summary>Theme loading and tiny builders so views can be composed in C# without a XAML compiler.</summary>
public static class Ui
{
    public static void LoadTheme(Application app)
    {
        using var s = typeof(Ui).Assembly.GetManifestResourceStream("HoodieCompanion.Theme.xaml")
                      ?? throw new InvalidOperationException("Theme.xaml resource missing");
        var dict = (ResourceDictionary)XamlReader.Load(s);
        app.Resources.MergedDictionaries.Add(dict);
    }

    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static Style Style(string key) => (Style)Application.Current.Resources[key];
    public static FontFamily Font => (FontFamily)Application.Current.Resources["UiFont"];

    public static TextBlock Text(string text, double size = 13, bool dim = false, FontWeight? weight = null, TextWrapping wrap = TextWrapping.Wrap)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = size,
            FontFamily = Font,
            Foreground = Brush(dim ? "TextDim" : "Text"),
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap,
            TextTrimming = wrap == TextWrapping.NoWrap ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        };
    }

    public static TextBlock Title(string text) => Text(text, 17, weight: FontWeights.SemiBold);

    public static TextBlock Caption(string text) => Text(text.ToUpperInvariant(), 11, dim: true, weight: FontWeights.SemiBold);

    public static Button Button(object content, Action onClick, string? style = null, string? tooltip = null)
    {
        var b = new Button { Content = content };
        if (style is not null) b.Style = Style(style);
        if (tooltip is not null) b.ToolTip = tooltip;
        b.Click += (_, _) => onClick();
        return b;
    }

    public static TextBox Input(string placeholder, Action<string>? onEnter = null)
    {
        var tb = new TextBox();
        var hint = Text(placeholder, 13, dim: true, wrap: TextWrapping.NoWrap);
        hint.IsHitTestVisible = false;
        hint.Margin = new Thickness(10, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        tb.Tag = hint;
        if (onEnter is not null)
        {
            tb.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    onEnter(tb.Text);
                    e.Handled = true;
                }
            };
        }
        return tb;
    }

    /// <summary>Wraps a text box with a placeholder overlay.</summary>
    public static Grid WithPlaceholder(TextBox tb)
    {
        var g = new System.Windows.Controls.Grid();
        g.Children.Add(tb);
        if (tb.Tag is TextBlock hint)
        {
            g.Children.Add(hint);
            void Sync() => hint.Visibility = string.IsNullOrEmpty(tb.Text) ? Visibility.Visible : Visibility.Collapsed;
            tb.TextChanged += (_, _) => Sync();
            Sync();
        }
        return g;
    }

    public static Border Card(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            Background = Brush("Surface"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = padding ?? new Thickness(12),
            Child = child,
        };
    }

    public static StackPanel Stack(Orientation o = Orientation.Vertical, double gap = 8, params UIElement[] children)
    {
        var sp = new StackPanel { Orientation = o };
        for (var i = 0; i < children.Length; i++)
        {
            var c = children[i];
            if (i > 0 && c is FrameworkElement fe)
            {
                fe.Margin = o == Orientation.Vertical ? new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom)
                                                      : new Thickness(fe.Margin.Left + gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom);
            }
            sp.Children.Add(c);
        }
        return sp;
    }

    public static UIElement Row(UIElement left, UIElement right, double gap = 8)
    {
        var g = new System.Windows.Controls.Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(left);
        if (right is FrameworkElement fe) fe.Margin = new Thickness(gap, 0, 0, 0);
        System.Windows.Controls.Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    /// <summary>Simple stroke icons (24x24 path data) consistent with the character's line work.</summary>
    public static Path Icon(string data, double size = 18, string brush = "Text")
    {
        var geo = System.Windows.Media.Geometry.Parse(data);
        return new Path
        {
            Data = geo,
            Stroke = Brush(brush),
            StrokeThickness = 1.8,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
        };
    }

    public static class Icons
    {
        public const string Backpack = "M 7 8 C 7 4 17 4 17 8 L 18 20 C 18 21 17 22 16 22 L 8 22 C 7 22 6 21 6 20 Z M 9 8 C 9 5.5 15 5.5 15 8 M 9 14 L 15 14";
        public const string Note = "M 6 3 L 15 3 L 19 7 L 19 21 L 6 21 Z M 15 3 L 15 7 L 19 7 M 9 11 L 16 11 M 9 15 L 16 15";
        public const string Bell = "M 6 16 L 6 11 C 6 7 9 5 12 5 C 15 5 18 7 18 11 L 18 16 L 20 18 L 4 18 Z M 10 20 C 10 22 14 22 14 20";
        public const string Timer = "M 12 21 C 16.4 21 20 17.4 20 13 C 20 8.6 16.4 5 12 5 C 7.6 5 4 8.6 4 13 C 4 17.4 7.6 21 12 21 Z M 12 9 L 12 13 L 15 15 M 10 2 L 14 2";
        public const string Pc = "M 3 5 L 21 5 L 21 16 L 3 16 Z M 9 20 L 15 20 M 12 16 L 12 20";
        public const string Gear = "M 12 15 C 13.7 15 15 13.7 15 12 C 15 10.3 13.7 9 12 9 C 10.3 9 9 10.3 9 12 C 9 13.7 10.3 15 12 15 Z M 12 2 L 12 5 M 12 19 L 12 22 M 2 12 L 5 12 M 19 12 L 22 12 M 4.9 4.9 L 7 7 M 17 17 L 19.1 19.1 M 4.9 19.1 L 7 17 M 17 7 L 19.1 4.9";
        public const string Home = "M 3 11 L 12 3 L 21 11 M 5 9 L 5 21 L 19 21 L 19 9";
        public const string Pin = "M 12 17 L 12 22 M 7 3 L 17 3 M 9 3 L 9 9 L 6 13 L 18 13 L 15 9 L 15 3";
        public const string Back = "M 15 5 L 8 12 L 15 19";
        public const string Close = "M 6 6 L 18 18 M 18 6 L 6 18";
        public const string Plus = "M 12 5 L 12 19 M 5 12 L 19 12";
        public const string Trash = "M 4 7 L 20 7 M 9 7 L 9 4 L 15 4 L 15 7 M 6 7 L 7 21 L 17 21 L 18 7";
        public const string Folder = "M 3 6 L 9 6 L 11 8 L 21 8 L 21 19 L 3 19 Z";
        public const string Link = "M 10 14 L 14 10 M 8 12 L 6 14 C 4.5 15.5 4.5 18 6 19.5 C 7.5 21 10 21 11.5 19.5 L 13.5 17.5 M 16 12 L 18 10 C 19.5 8.5 19.5 6 18 4.5 C 16.5 3 14 3 12.5 4.5 L 10.5 6.5";
        public const string More = "M 5 12 L 5.01 12 M 12 12 L 12.01 12 M 19 12 L 19.01 12";
        public const string Down = "M 12 4 L 12 20 M 6 14 L 12 20 L 18 14";
        public const string Up = "M 12 20 L 12 4 M 6 10 L 12 4 L 18 10";
        public const string Hand = "M 8 13 L 8 5 C 8 3.5 10 3.5 10 5 L 10 11 M 10 10 L 10 3.5 C 10 2 12 2 12 3.5 L 12 10 M 12 10 L 12 4.5 C 12 3 14 3 14 4.5 L 14 11 M 14 11 L 14 7 C 14 5.5 16 5.5 16 7 L 16 15 C 16 19 13 21 11 21 C 8 21 6 19 5 16 L 4 13 C 3.5 11.5 5.5 11 6.5 12.5 L 8 14";
    }

    /// <summary>A panel-like borderless window chrome: rounded card with a soft shadow.</summary>
    public static Border Chrome(UIElement content)
    {
        return new Border
        {
            Background = Brush("Bg"),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(14),
            Child = content,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.45, Color = Colors.Black },
        };
    }

    public static ScrollViewer Scroll(UIElement content, double maxHeight = double.PositiveInfinity)
    {
        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = maxHeight,
        };
    }

    public static RadioButton Chip(string label, bool isChecked, string group, Action onChecked, string? tooltip = null)
    {
        var rb = new RadioButton { Content = label, GroupName = group, IsChecked = isChecked, Style = Style("Chip"), ToolTip = tooltip };
        rb.Checked += (_, _) => onChecked();
        return rb;
    }

    public static UniformGrid Grid(int columns, params UIElement[] items)
    {
        var g = new UniformGrid { Columns = columns };
        foreach (var i in items) g.Children.Add(i);
        return g;
    }
}
