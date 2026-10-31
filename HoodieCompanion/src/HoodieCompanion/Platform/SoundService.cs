using System;
using System.IO;
using System.Media;

namespace HoodieCompanion.Platform;

public enum SoundCue
{
    ItemReceived,
    Reminder,
    Timer,
    Landing,
}

/// <summary>Soft, synthesized sounds (no audio files). Quiet by design; one master toggle.</summary>
public sealed class SoundService
{
    private readonly Func<bool> _enabled;
    private readonly byte[] _item;
    private readonly byte[] _chime;
    private readonly byte[] _thud;
    private DateTime _lastPlayed = DateTime.MinValue;

    public SoundService(Func<bool> enabled)
    {
        _enabled = enabled;
        _item = Tone(new[] { (880.0, 0.06), (1320.0, 0.09) }, 0.18);
        _chime = Tone(new[] { (784.0, 0.16), (1046.5, 0.28) }, 0.22);
        _thud = Tone(new[] { (140.0, 0.07) }, 0.2);
    }

    public void Play(SoundCue cue)
    {
        if (!_enabled()) return;
        if ((DateTime.Now - _lastPlayed).TotalMilliseconds < 120) return;
        _lastPlayed = DateTime.Now;
        var data = cue switch
        {
            SoundCue.ItemReceived => _item,
            SoundCue.Landing => _thud,
            _ => _chime,
        };
        try
        {
            var player = new SoundPlayer(new MemoryStream(data));
            player.Play();
        }
        catch (Exception ex)
        {
            Log.Debug("sound failed: " + ex.Message);
        }
    }

    /// <summary>Builds a 16-bit mono WAV of consecutive sine notes with soft envelopes.</summary>
    private static byte[] Tone((double Freq, double Seconds)[] notes, double volume)
    {
        const int rate = 22050;
        var total = 0;
        foreach (var n in notes) total += (int)(n.Seconds * rate);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + total * 2);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write("data"u8.ToArray());
        w.Write(total * 2);
        foreach (var (freq, seconds) in notes)
        {
            var count = (int)(seconds * rate);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / rate;
                var env = Math.Min(1, i / (rate * 0.008)) * Math.Exp(-4.5 * i / (double)count);
                var s = Math.Sin(2 * Math.PI * freq * t) * env * volume;
                w.Write((short)(s * short.MaxValue));
            }
        }
        w.Flush();
        return ms.ToArray();
    }
}
