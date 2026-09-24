using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using HoodieCompanion.Companion.Animation;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Companion.Physics;
using HoodieCompanion.Companion.Rendering;
using HoodieCompanion.Features.Backpack;
using HoodieCompanion.Geometry;
using HoodieCompanion.Platform;

namespace HoodieCompanion.UI;

/// <summary>
/// Transparent, click-through-where-empty, never-activating window that hosts the character rig.
/// The window is a small box that follows Hoodie; world placement happens in physical pixels.
/// </summary>
public sealed class PetWindow : Window
{
    public const double BoxDip = 300;

    private readonly Canvas _root = new() { ClipToBounds = false };
    private readonly Canvas _stage = new() { Width = 559, Height = 895 };
    private readonly Canvas _fxStage = new() { Width = 559, Height = 895, IsHitTestVisible = false };
    private readonly MatrixTransform _stageTransform = new();
    private readonly MatrixTransform _fxTransform = new();
    private double _dpi = 1;
    private int _lastX = int.MinValue, _lastY, _lastW, _lastH;
    private bool _pressed;
    private bool _dragging;
    private Vec2 _pressPx;

    public PetWindow()
    {
        Title = "Hoodie Companion";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        Width = BoxDip;
        Height = BoxDip;
        Left = -10000;
        Top = -10000;
        AllowDrop = true;
        UseLayoutRounding = false;

        Rig = new CharacterRig();
        Effects = new EffectLayer();
        _stage.Children.Add(Rig);
        _fxStage.Children.Add(Effects);
        _stage.RenderTransform = _stageTransform;
        _fxStage.RenderTransform = _fxTransform;
        _root.Children.Add(_stage);
        _root.Children.Add(_fxStage);
        Content = _root;
        RenderOptions.SetEdgeMode(_stage, EdgeMode.Unspecified);

        SourceInitialized += (_, _) =>
        {
            Hwnd = new WindowInteropHelper(this).Handle;
            WindowInterop.MakeToolWindow(this, noActivate: true);
            _dpi = WindowInterop.WindowScale(this);
            HwndSource.FromHwnd(Hwnd)?.AddHook(WndProc);
        };
        DpiChanged += (_, e) => _dpi = e.NewDpi.DpiScaleX;

        MouseLeftButtonDown += OnLeftDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnLeftUp;
        MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            RightClicked?.Invoke();
        };
        LostMouseCapture += (_, _) =>
        {
            if (_dragging) Released?.Invoke(MouseService.Cursor());
            _dragging = false;
            _pressed = false;
        };

        DragEnter += (_, e) =>
        {
            e.Effects = DropHandler.CanAccept(e.Data) ? DragDropEffects.Link | DragDropEffects.Copy : DragDropEffects.None;
            if (e.Effects != DragDropEffects.None) ItemDragEnter?.Invoke();
            e.Handled = true;
        };
        DragOver += (_, e) =>
        {
            e.Effects = DropHandler.CanAccept(e.Data) ? DragDropEffects.Link | DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        DragLeave += (_, e) =>
        {
            ItemDragLeave?.Invoke();
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            var items = DropHandler.Extract(e.Data);
            e.Handled = true;
            if (items.Count > 0) ItemsDropped?.Invoke(items);
            else ItemDragLeave?.Invoke();
        };
    }

    public CharacterRig Rig { get; }
    public EffectLayer Effects { get; }
    public IntPtr Hwnd { get; private set; }
    public double RenderScale => _dpi;

    /// <summary>Asked when a drag gesture starts; return false to ignore (e.g. grab disabled).</summary>
    public Func<Vec2, bool>? TryBeginGrab { get; set; }

    public event Action<Vec2>? Released;
    public event Action? Clicked;
    public event Action? RightClicked;
    public event Action? ItemDragEnter;
    public event Action? ItemDragLeave;
    public event Action<IReadOnlyList<string>>? ItemsDropped;

    public bool IsDragging => _dragging;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            // Clicking Hoodie never steals focus from the user's work.
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragging = false;
        _pressPx = MouseService.Cursor();
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragging) return;
        var now = MouseService.Cursor();
        if (Vec2.Distance(now, _pressPx) / _dpi < 5) return;
        _dragging = TryBeginGrab?.Invoke(_pressPx) ?? false;
        if (!_dragging)
        {
            _pressed = false;
            ReleaseMouseCapture();
        }
    }

    private void OnLeftUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        e.Handled = true;
        var wasDragging = _dragging;
        _pressed = false;
        _dragging = false;
        ReleaseMouseCapture();
        if (wasDragging) Released?.Invoke(MouseService.Cursor());
        else Clicked?.Invoke();
    }

    /// <summary>Draws one frame and moves the window (physical pixels) to follow Hoodie.</summary>
    public void Render(in RenderState rs, double userScale, bool topmost, bool reducedMotion)
    {
        if (!rs.Visible)
        {
            if (_root.Visibility != Visibility.Hidden) _root.Visibility = Visibility.Hidden;
            return;
        }
        if (_root.Visibility != Visibility.Visible) _root.Visibility = Visibility.Visible;

        Rig.Apply(rs.Pose);

        var t = rs.Transform;
        var boxPx = BoxDip * userScale * rs.MonitorScale;
        var center = t.LocalToWorld(BodyMetrics.CenterLocal);
        var x = (int)Math.Floor(center.X - boxPx / 2);
        var y = (int)Math.Floor(center.Y - boxPx / 2);
        var size = (int)Math.Ceiling(boxPx);
        if (x != _lastX || y != _lastY || size != _lastW || size != _lastH)
        {
            if (size != _lastW || size != _lastH) WindowInterop.SetBounds(Hwnd, x, y, size, size, topmost);
            else WindowInterop.Move(Hwnd, x, y);
            _lastX = x;
            _lastY = y;
            _lastW = size;
            _lastH = size;
        }

        // Stage matrix: local -> mirror -> anchor -> scale -> tilt -> window DIPs.
        var k = t.RefToPx / _dpi;
        var mirroredAnchor = RigTransform.Mirror(t.AnchorLocal, t.Facing);
        var m = t.Facing > 0 ? new Matrix(-1, 0, 0, 1, 2 * RigTransform.AxisX, 0) : Matrix.Identity;
        m.Translate(-mirroredAnchor.X, -mirroredAnchor.Y);
        m.Scale(k, k);
        m.Rotate(t.Tilt);
        var ox = (t.AnchorWorld.X - x) / _dpi;
        var oy = (t.AnchorWorld.Y - y) / _dpi;
        m.Translate(ox, oy);
        _stageTransform.Matrix = m;

        // Effects are never mirrored (text must stay readable).
        var fm = Matrix.Identity;
        fm.Translate(-mirroredAnchor.X, -mirroredAnchor.Y);
        fm.Scale(k, k);
        fm.Rotate(t.Tilt);
        fm.Translate(ox, oy);
        _fxTransform.Matrix = fm;
        var head = Mirror(Rig.HeadTop, t.Facing);
        var feet = Mirror(Rig.Feet, t.Facing);
        Effects.Update(rs.Effect, rs.EffectTime, head, feet, reducedMotion);
        Effects.Opacity = rs.Pose.Opacity;
    }

    private static Point Mirror(Point p, int facing) => facing > 0 ? new Point(2 * RigTransform.AxisX - p.X, p.Y) : p;

    /// <summary>Physical-pixel rectangle currently occupied by the window.</summary>
    public RectD WindowRectPx => new(_lastX, _lastY, _lastW, _lastH);
}
