using System;
using HoodieCompanion.Companion.Behavior;
using HoodieCompanion.Settings;
using WinForms = System.Windows.Forms;

namespace HoodieCompanion.UI;

/// <summary>System tray presence: always-available emergency control and quick commands.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppHost _host;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ToolStripMenuItem _hideItem;
    private readonly WinForms.ToolStripMenuItem _presence;

    public TrayIcon(AppHost host)
    {
        _host = host;
        var menu = new WinForms.ContextMenuStrip();
        _hideItem = new WinForms.ToolStripMenuItem("Hide Hoodie", null, (_, _) => _host.SetEmergencyHidden(!_host.EmergencyHidden)) { ShortcutKeyDisplayString = "Ctrl+Alt+H" };
        menu.Items.Add(_hideItem);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Come here", null, (_, _) => _host.Command(PetCommand.ComeHere));
        menu.Items.Add("Go home", null, (_, _) => _host.Command(PetCommand.GoHome));
        menu.Items.Add("Stay here / You're free", null, (_, _) => _host.Command(_host.Territory.Anchor is null ? PetCommand.StayHere : PetCommand.YoureFree));

        _presence = new WinForms.ToolStripMenuItem("Presence");
        foreach (var mode in Enum.GetValues<PresenceMode>())
        {
            var m = mode;
            var label = m switch
            {
                PresenceMode.Alone => "Leave me alone",
                PresenceMode.Quiet => "Be quiet",
                PresenceMode.Play => "Let's play",
                _ => m.ToString(),
            };
            _presence.DropDownItems.Add(new WinForms.ToolStripMenuItem(label, null, (_, _) => _host.SetMode(m)) { Tag = m });
        }
        _presence.DropDownItems.Add(new WinForms.ToolStripSeparator());
        _presence.DropDownItems.Add("Come back", null, (_, _) => _host.Command(PetCommand.ComeBack));
        menu.Items.Add(_presence);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Backpack…", null, (_, _) => _host.OpenPanel(PanelPage.Backpack));
        menu.Items.Add("Notes…", null, (_, _) => _host.OpenPanel(PanelPage.Notes));
        menu.Items.Add("Reminder…", null, (_, _) => _host.OpenPanel(PanelPage.Reminder));
        menu.Items.Add("Timer…", null, (_, _) => _host.OpenPanel(PanelPage.Timer));
        menu.Items.Add("PC Status…", null, (_, _) => _host.OpenPanel(PanelPage.PcStatus));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Territory editor…", null, (_, _) => _host.ShowTerritoryEditor());
        menu.Items.Add("Settings…", null, (_, _) => _host.ShowSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit Hoodie Companion", null, (_, _) => _host.Exit());
        menu.Opening += (_, _) => Refresh();

        _icon = new WinForms.NotifyIcon
        {
            Text = "Hoodie Companion",
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left)
            {
                if (_host.EmergencyHidden) _host.SetEmergencyHidden(false);
                else _host.OpenPanel(PanelPage.Home);
            }
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            using var s = typeof(TrayIcon).Assembly.GetManifestResourceStream("HoodieCompanion.hoodie.ico");
            if (s is not null) return new System.Drawing.Icon(s, new System.Drawing.Size(16, 16));
        }
        catch
        {
        }
        return System.Drawing.SystemIcons.Application;
    }

    public void Refresh()
    {
        _hideItem.Text = _host.EmergencyHidden ? "Show Hoodie" : "Hide Hoodie";
        foreach (var item in _presence.DropDownItems)
        {
            if (item is WinForms.ToolStripMenuItem mi && mi.Tag is PresenceMode m) mi.Checked = _host.Pet.Mode == m;
        }
    }

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
