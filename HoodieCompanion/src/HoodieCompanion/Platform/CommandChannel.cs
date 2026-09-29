using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace HoodieCompanion.Platform;

/// <summary>
/// Tiny local named pipe that lets a second launch ("HoodieCompanion.exe --cmd comehere") hand a command
/// and the pointer position to the running instance. Local to the user session; plain text, one line.
/// </summary>
public sealed class CommandChannel : IDisposable
{
    private static string PipeName => "HoodieCompanion.Cmd." + Environment.UserName;
    private readonly Action<string, double, double> _onCommand;
    private readonly Thread _thread;
    private volatile bool _stop;

    public CommandChannel(Action<string, double, double> onCommand)
    {
        _onCommand = onCommand;
        _thread = new Thread(Loop) { IsBackground = true, Name = "command-channel" };
        _thread.Start();
    }

    private void Loop()
    {
        while (!_stop)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);
                server.WaitForConnection();
                using var reader = new StreamReader(server);
                var line = reader.ReadLine();
                if (line is null) continue;
                var parts = line.Split(' ');
                if (parts.Length < 3) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
                _onCommand(parts[0], x, y);
            }
            catch (Exception ex)
            {
                if (_stop) return;
                Log.Debug("command channel: " + ex.Message);
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>Sends a command from a second process. Returns false if no instance is listening.</summary>
    public static bool Send(string command, double x, double y)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client);
            w.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{command} {x} {y}"));
            w.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop = true;
        // Unblock WaitForConnection.
        try { Send("noop", 0, 0); } catch { }
    }
}
