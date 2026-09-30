using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HoodieCompanion.Platform;

namespace HoodieCompanion.UI;

public sealed record AlertAction(string Label, Action OnClick, bool Primary = false);

public sealed record AlertRequest(string Title, string Message, IReadOnlyList<AlertAction> Actions, Action? OnDismissed = null, string Icon = Ui.Icons.Bell);

/// <summary>
/// Small context card that appears above Hoodie (reminders, finished timers, missing items, first-run hello).
/// Shown without stealing focus; one at a time, queued.
/// </summary>
public sealed class AlertCard : Window
{
    private readonly AppHost _host;
    private readonly Queue<AlertRequest> _queue = new();
    private readonly ContentControl _body = new();
    private readonly Border _chrome;
    private AlertRequest? _current;

    public AlertCard(AppHost host)
    {
        _host = host;
        Title = "Hoodie";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 300;
        SizeToContent = SizeToContent.Height;
        FontFamily = Ui.Font;
        _chrome = Ui.Chrome(_body);
        Content = _chrome;
        SourceInitialized += (_, _) => WindowInterop.MakeToolWindow(this, noActivate: true);
        SizeChanged += (_, _) => Place();
    }

    public bool IsShowing => _current is not null;

    public void Enqueue(AlertRequest request)
    {
        _queue.Enqueue(request);
        if (_current is null) Next();
    }

    private void Next()
    {
        if (_queue.Count == 0)
        {
            _current = null;
            Hide();
            return;
        }
        _current = _queue.Dequeue();
        var r = _current;

        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(Ui.Icon(r.Icon, 18, "Accent"));
        var t = Ui.Title(r.Title);
        t.Margin = new Thickness(8, 0, 0, 0);
        t.FontSize = 15;
        header.Children.Add(t);

        var msg = Ui.Text(r.Message, 13);
        msg.Margin = new Thickness(0, 8, 0, 12);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var a in r.Actions)
        {
            var action = a;
            var b = Ui.Button(a.Label, () =>
            {
                Finish(dismissed: false);
                action.OnClick();
            }, a.Primary ? "AccentButton" : null);
            b.Margin = new Thickness(6, 0, 0, 0);
            buttons.Children.Add(b);
        }

        var sp = new StackPanel { Margin = new Thickness(16, 14, 16, 14) };
        sp.Children.Add(header);
        sp.Children.Add(msg);
        sp.Children.Add(buttons);
        _body.Content = sp;

        if (!IsVisible)
        {
            _chrome.Opacity = 0;
            Show();
        }
        Place();
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(_host.Settings.ReducedMotion ? 120 : 200));
        _chrome.BeginAnimation(OpacityProperty, anim);
    }

    /// <summary>Closes the current card (e.g. acknowledged elsewhere).</summary>
    public void Finish(bool dismissed)
    {
        var r = _current;
        _current = null;
        if (dismissed) r?.OnDismissed?.Invoke();
        Next();
    }

    /// <summary>Keeps the card floating above Hoodie's head.</summary>
    public void Place()
    {
        if (!IsVisible) return;
        var pet = _host.PetBoundsPx;
        var mon = _host.World.NearestMonitor(pet.Center);
        var s = mon.Scale;
        var w = Width * s;
        var h = (ActualHeight > 0 ? ActualHeight : 180) * s;
        var x = pet.Center.X - w / 2;
        var y = pet.Top - h + 10 * s;
        var wa = mon.WorkArea;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - w));
        if (y < wa.Top) y = Math.Min(wa.Bottom - h, pet.Bottom);
        WindowInterop.Move(WindowInterop.Handle(this), (int)Math.Round(x), (int)Math.Round(y));
    }
}
