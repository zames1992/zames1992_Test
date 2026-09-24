using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using HoodieCompanion.Features.Notes;
using HoodieCompanion.Platform;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>
/// A note pinned to the desktop: a small Sticky-Notes-like card that floats above everything, can be moved
/// (drag the top strip), resized (bottom-right grip), edited in place and unpinned.
/// </summary>
public sealed class StickyNoteWindow : Window
{
    private readonly NoteService _notes;
    private readonly string _id;
    private readonly TextBox _text;
    private readonly Border _card;
    private readonly Border _header;
    private readonly DispatcherTimer _saveSoon;

    public StickyNoteWindow(NoteService notes, Note note)
    {
        _notes = notes;
        _id = note.Id;
        Title = "Hoodie note";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        MinWidth = 160;
        MinHeight = 120;
        FontFamily = Ui.Font;
        var p = note.Placement ?? DefaultPlacement();
        Left = p.Left;
        Top = p.Top;
        Width = p.Width;
        Height = p.Height;

        _header = new Border { Height = 26, CornerRadius = new CornerRadius(6, 6, 0, 0), Cursor = Cursors.SizeAll };
        var bar = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 4, 0) };
        var unpin = Small(Ui.Icons.Close, T("Unpin from the desktop"), () => _notes.SetPinned(_id, false));
        DockPanel.SetDock(unpin, Dock.Right);
        bar.Children.Add(unpin);
        var color = Small(Ui.Icons.More, T("Colour"), NextColor);
        DockPanel.SetDock(color, Dock.Right);
        bar.Children.Add(color);
        var pin = Ui.Icon(Ui.Icons.Pin, 13);
        pin.Margin = new Thickness(2, 0, 0, 0);
        pin.IsHitTestVisible = false;
        bar.Children.Add(pin);
        _header.Child = bar;
        _header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement fe && fe.TemplatedParent is Button) return;
            try { DragMove(); } catch { }
            SavePlacement();
        };

        _text = new TextBox
        {
            Text = note.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _saveSoon = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.8) };
        _saveSoon.Tick += (_, _) =>
        {
            _saveSoon.Stop();
            if (!string.IsNullOrWhiteSpace(_text.Text)) _notes.Update(_id, _text.Text, notify: false);
        };
        _text.TextChanged += (_, _) => { _saveSoon.Stop(); _saveSoon.Start(); };
        _text.LostKeyboardFocus += (_, _) => Flush();

        var dock = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        dock.Children.Add(_text);
        _card = new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = dock,
            Margin = new Thickness(8),
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.35, Color = Colors.Black },
        };
        Content = _card;
        Apply(note);

        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        LocationChanged += (_, _) => SaveSoonPlacement();
        SizeChanged += (_, _) => SaveSoonPlacement();
        Closing += (_, _) => { Flush(); SavePlacement(); };
    }

    public string NoteId => _id;

    private Button Small(string icon, string tip, Action a)
    {
        var b = Ui.Button(Ui.Icon(icon, 11), a, "GhostButton", tip);
        b.Padding = new Thickness(4);
        b.VerticalAlignment = VerticalAlignment.Center;
        return b;
    }

    private static NotePlacement DefaultPlacement()
    {
        var wa = SystemParameters.WorkArea;
        return new NotePlacement { Left = wa.Right - 280, Top = wa.Top + 60, Width = 240, Height = 220 };
    }

    public void Apply(Note note)
    {
        var (back, header, text) = NoteColors.Palette(note.Color);
        _card.Background = Brush(back);
        _header.Background = Brush(header);
        _text.Foreground = Brush(text);
        _text.CaretBrush = Brush(text);
        if (!_text.IsKeyboardFocusWithin && _text.Text != note.Text) _text.Text = note.Text;
        foreach (var path in LogicalChildren(_header).OfType<System.Windows.Shapes.Path>()) path.Stroke = Brush(text);
    }

    private static IEnumerable<object> LogicalChildren(DependencyObject o)
    {
        foreach (var c in LogicalTreeHelper.GetChildren(o))
        {
            yield return c;
            if (c is DependencyObject d)
                foreach (var cc in LogicalChildren(d)) yield return cc;
        }
    }

    private static Brush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private void NextColor()
    {
        var n = _notes.Find(_id);
        if (n is null) return;
        var i = Array.IndexOf(NoteColors.All, n.Color);
        _notes.SetColor(_id, NoteColors.All[(i + 1) % NoteColors.All.Length]);
    }

    private void Flush()
    {
        _saveSoon.Stop();
        if (!string.IsNullOrWhiteSpace(_text.Text)) _notes.Update(_id, _text.Text, notify: false);
    }

    private DispatcherTimer? _placeSoon;

    private void SaveSoonPlacement()
    {
        _placeSoon ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        _placeSoon.Tick -= OnPlaceTick;
        _placeSoon.Tick += OnPlaceTick;
        _placeSoon.Stop();
        _placeSoon.Start();
    }

    private void OnPlaceTick(object? sender, EventArgs e)
    {
        _placeSoon?.Stop();
        SavePlacement();
    }

    private void SavePlacement()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        _notes.SetPlacement(_id, new NotePlacement { Left = Left, Top = Top, Width = ActualWidth > 0 ? ActualWidth : Width, Height = ActualHeight > 0 ? ActualHeight : Height });
    }
}

/// <summary>Keeps one desktop window per pinned note in sync with the notes store.</summary>
public sealed class StickyNotes
{
    private readonly NoteService _notes;
    private readonly Dictionary<string, StickyNoteWindow> _windows = new();

    public StickyNotes(NoteService notes)
    {
        _notes = notes;
        _notes.Changed += Sync;
    }

    public int Count => _windows.Count;

    public void Sync()
    {
        var pinned = _notes.All.Where(n => n.IsPinned && !n.IsCompleted).ToDictionary(n => n.Id);
        foreach (var id in _windows.Keys.Where(id => !pinned.ContainsKey(id)).ToList())
        {
            _windows[id].Close();
            _windows.Remove(id);
        }
        foreach (var (id, note) in pinned)
        {
            if (_windows.TryGetValue(id, out var w))
            {
                w.Apply(note);
                continue;
            }
            w = new StickyNoteWindow(_notes, note);
            _windows[id] = w;
            w.Show();
        }
    }

    public void SetVisible(bool visible)
    {
        foreach (var w in _windows.Values)
        {
            if (visible) w.Show(); else w.Hide();
        }
    }

    public void CloseAll()
    {
        foreach (var w in _windows.Values) w.Close();
        _windows.Clear();
    }
}
