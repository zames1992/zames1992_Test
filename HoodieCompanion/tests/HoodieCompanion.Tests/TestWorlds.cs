using HoodieCompanion.Geometry;

namespace HoodieCompanion.Tests;

internal static class TestWorlds
{
    public static MonitorInfo Mon(string id, int index, double x, double y, double w, double h, double scale = 1, bool primary = false, double taskbar = 40) =>
        new(id, index, new RectD(x, y, w, h), new RectD(x, y, w, h - taskbar), scale, primary);

    /// <summary>[ 1 ][ 2 ] side by side, 2 has no taskbar and a higher DPI.</summary>
    public static WorldGeometry SideBySide() => new(new[]
    {
        Mon("A", 0, 0, 0, 1920, 1080, 1.0, primary: true),
        Mon("B", 1, 1920, 0, 2560, 1440, 1.5, taskbar: 0),
    });

    /// <summary>Monitor 2 sits above-right of monitor 1, with negative Y.</summary>
    public static WorldGeometry Stacked() => new(new[]
    {
        Mon("A", 0, 0, 0, 1920, 1080, 1.0, primary: true),
        Mon("B", 1, 600, -1080, 1920, 1080, 1.0, taskbar: 0),
    });

    /// <summary>Secondary monitor to the left at negative X.</summary>
    public static WorldGeometry NegativeLeft() => new(new[]
    {
        Mon("A", 0, 0, 0, 1920, 1080, 1.0, primary: true),
        Mon("B", 1, -1280, 200, 1280, 1024, 1.25, taskbar: 0),
    });

    /// <summary>Monitor B to the right sits 300 px higher (its floor is above A's floor).</summary>
    public static WorldGeometry OffsetHigher() => new(new[]
    {
        Mon("A", 0, 0, 0, 1920, 1080, 1.0, primary: true),
        Mon("B", 1, 1920, -300, 1920, 1080, 1.0, taskbar: 0),
    });

    public static WorldGeometry Single() => new(new[] { Mon("A", 0, 0, 0, 1920, 1080, 1.0, primary: true) });
}
