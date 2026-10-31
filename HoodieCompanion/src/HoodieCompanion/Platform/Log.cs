using System;
using System.IO;

namespace HoodieCompanion.Platform;

/// <summary>Tiny local file log (%LocalAppData%\HoodieCompanion\logs). Nothing leaves the machine.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _path;

    public static bool Verbose { get; set; }

    /// <summary>Number of errors logged this session (used by the QA report).</summary>
    public static int ErrorCount;

    public static void Init(string root)
    {
        try
        {
            var dir = Path.Combine(root, "logs");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "hoodie.log");
            if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000)
            {
                File.Copy(_path, _path + ".old", true);
                File.WriteAllText(_path, "");
            }
        }
        catch
        {
            _path = null;
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Debug(string message) { if (Verbose) Write("DBG ", message); }
    public static void Error(string message, Exception? ex = null)
    {
        System.Threading.Interlocked.Increment(ref ErrorCount);
        Write("ERR ", ex is null ? message : message + ": " + ex);
    }

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        try
        {
            lock (Gate) File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
        }
        catch
        {
            // logging must never crash the companion
        }
    }
}
