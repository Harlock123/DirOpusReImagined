using System.Collections.Generic;

namespace DirOpusReImagined.FileSystem;

/// <summary>
/// Transfer speed over the last few seconds of <em>active</em> time. Fed with an operation's
/// active clock (which stops while it's paused) and its running byte total, so a pause neither
/// drags the speed down nor stretches the time-remaining estimate once it resumes.
/// </summary>
public sealed class SpeedMeter
{
    private readonly double _windowSeconds;
    private readonly List<(double Seconds, long Bytes)> _samples = new();

    public SpeedMeter(double windowSeconds = 5) => _windowSeconds = windowSeconds;

    /// <summary>Adds a sample: <paramref name="activeSeconds"/> of work so far, <paramref name="bytesDone"/> in total.</summary>
    public void Add(double activeSeconds, long bytesDone)
    {
        // Bytes going backwards means a new count started; begin a fresh window.
        if (_samples.Count > 0 && bytesDone < _samples[^1].Bytes) _samples.Clear();

        _samples.Add((activeSeconds, bytesDone));

        // Drop samples that fall outside the window, keeping the newest one before it as the
        // window's starting point.
        int drop = 0;
        while (drop < _samples.Count - 2 && activeSeconds - _samples[drop + 1].Seconds >= _windowSeconds)
            drop++;
        if (drop > 0) _samples.RemoveRange(0, drop);
    }

    /// <summary>Bytes per second over the window, or null until there is half a second of data.</summary>
    public double? BytesPerSecond
    {
        get
        {
            if (_samples.Count < 2) return null;
            var (t0, b0) = _samples[0];
            var (t1, b1) = _samples[^1];
            double dt = t1 - t0;
            if (dt < 0.5) return null;
            return (b1 - b0) / dt;
        }
    }
}
