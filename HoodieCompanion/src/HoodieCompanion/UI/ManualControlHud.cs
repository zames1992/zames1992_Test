using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Platform;
using static HoodieCompanion.UI.L;

namespace HoodieCompanion.UI;

/// <summary>
/// The little "you're driving" pill shown above Hoodie while manual control is on. It is the only window that
/// reads the keys: it takes keyboard focus, so nothing is read from other apps, and losing focus ends manual
/// control. Keys are turned into a <see cref="ManualInput"/> each frame and forgotten; nothing is stored.
/// </summary>
public sealed class ManualControlHud : Window
{
    private readonly AppHost _host;
    private readonly Border _chrome;
    private int _moveX;
    private bool _run, _jump, _up, _down, _sit;
    private bool _closing;

    public ManualControlHud(AppHost host)
    {
        _host = host;
        Title = "Hoodie";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Ui.Font;
        Focusable = true;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var icon = Ui.Icon(Ui.Icons.Gamepad, 15, "Accent");
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(icon);
        row.Children.Add(Ui.Text(T("You're driving"), 12.5, weight: FontWeights.SemiBold, wrap: TextWrapping.NoWrap));
        var hint = Ui.Text("  " + T("← → walk · Shift run · Space jump · ↑ climb · ↓ hop down · E sit · Esc done"), 11.5, dim: true, wrap: TextWrapping.NoWrap);
        hint.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(hint);
        _chrome = new Border
        {
            Background = Ui.Brush("Bg"),
            BorderBrush = Ui.Brush("Accent"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(10),
            Child = row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black },
        };
        Content = _chrome;

        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: false);
        PreviewKeyDown += (_, e) => { Key(e.Key, true); e.Handled = true; };
        PreviewKeyUp += (_, e) => { Key(e.Key, false); e.Handled = true; };
        // The user went to another window: stop driving (keys must never be read from elsewhere).
        Deactivated += (_, _) => { if (!_closing) _host.SetManualControl(false); };
        Closing += (_, e) =>
        {
            if (_host.IsExiting) return;
            e.Cancel = true;
            Hide();
        };
    }

    private void Key(Key key, bool down)
    {
        switch (key)
        {
            case System.Windows.Input.Key.Left or System.Windows.Input.Key.A:
                if (down) _moveX = -1; else if (_moveX < 0) _moveX = 0;
                break;
            case System.Windows.Input.Key.Right or System.Windows.Input.Key.D:
                if (down) _moveX = 1; else if (_moveX > 0) _moveX = 0;
                break;
            case System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift:
                _run = down;
                break;
            case System.Windows.Input.Key.Space:
                if (down) _jump = true;
                break;
            case System.Windows.Input.Key.Up or System.Windows.Input.Key.W:
                if (down) _up = true;
                break;
            case System.Windows.Input.Key.Down or System.Windows.Input.Key.S:
                if (down) _down = true;
                break;
            case System.Windows.Input.Key.E:
                if (down) _sit = true;
                break;
            case System.Windows.Input.Key.Escape:
                if (down) _host.SetManualControl(false);
                break;
        }
    }

    /// <summary>This frame's input; one-shot keys (jump, climb, sit) are consumed.</summary>
    public ManualInput Take()
    {
        var k = new ManualInput { MoveX = _moveX, Run = _run, Jump = _jump, Up = _up, Down = _down, Sit = _sit };
        _jump = _up = _down = _sit = false;
        return k;
    }

    public void Open()
    {
        _closing = false;
        _moveX = 0;
        _run = _jump = _up = _down = _sit = false;
        if (!IsVisible)
        {
            SizeToContent = SizeToContent.WidthAndHeight;
            Show();
            UpdateLayout();
            // PlaceWindowPx needs a real Width/Height (SizeToContent leaves them NaN).
            Width = ActualWidth;
            Height = ActualHeight;
            SizeToContent = SizeToContent.Manual;
        }
        Place();
        Activate();
        Focus();
    }

    public void CloseHud()
    {
        _closing = true;
        _moveX = 0;
        if (IsVisible) Hide();
    }

    /// <summary>Keeps the pill above Hoodie's head (called every frame while driving).</summary>
    public void Place()
    {
        if (!IsVisible) return;
        var pet = _host.PetBoundsPx;
        var mon = _host.World.NearestMonitor(pet.Center);
        var s = mon.Scale;
        var w = Width * s;
        var h = Height * s;
        var wa = mon.WorkArea;
        var x = Math.Clamp(pet.Center.X - w / 2, wa.Left, Math.Max(wa.Left, wa.Right - w));
        var y = pet.Top - h + 6 * s;
        if (y < wa.Top) y = pet.Bottom - 6 * s;
        WindowInterop.PlaceWindowPx(this, x, y, s);
    }
}
