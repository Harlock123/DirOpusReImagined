using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace DirOpusReImagined.FileSystem;

/// <summary>A ready-made conversion: what it's called, the file type it makes, and ffmpeg's settings for it.</summary>
/// <param name="Extension">Without the dot; empty to keep the source's (the plain cut).</param>
/// <param name="AudioOnly">Makes an audio file, so a video's picture is dropped.</param>
/// <param name="CanResize">The picture size can be changed (not for audio, or a cut that copies the streams as they are).</param>
public sealed record MediaPreset(string Name, string Extension, string[] Args, bool AudioOnly, bool CanResize);

/// <summary>One conversion: the source, where the result goes, and the optional part to keep and picture height.</summary>
public sealed record MediaJob(string Source, string Target, MediaPreset Preset, TimeSpan? Start = null, TimeSpan? End = null, int? Height = null);

/// <summary>
/// Audio and video conversion with ffmpeg (the one <see cref="VideoFrames"/> finds): turn a
/// video into an MP3, make a video smaller, change the format, or cut out a part. Each file is a
/// <see cref="FileOperation"/> in the queue, with progress read from ffmpeg's <c>-progress</c>
/// output. The result is written beside a temporary name and renamed when ffmpeg finishes, so a
/// cancelled or failed conversion never leaves half a file under the real name, and nothing is
/// ever overwritten.
/// </summary>
public static class MediaConvert
{
    public static readonly IReadOnlyList<MediaPreset> Presets = new[]
    {
        new MediaPreset("MP3 audio (192 kbit/s)", "mp3", new[] { "-vn", "-c:a", "libmp3lame", "-b:a", "192k" }, true, false),
        new MediaPreset("AAC audio, .m4a (192 kbit/s)", "m4a", new[] { "-vn", "-c:a", "aac", "-b:a", "192k" }, true, false),
        new MediaPreset("Opus audio (128 kbit/s)", "opus", new[] { "-vn", "-c:a", "libopus", "-b:a", "128k" }, true, false),
        new MediaPreset("FLAC audio (lossless)", "flac", new[] { "-vn", "-c:a", "flac" }, true, false),
        new MediaPreset("WAV audio (uncompressed)", "wav", new[] { "-vn", "-c:a", "pcm_s16le" }, true, false),
        new MediaPreset("MP4 video, H.264 (smaller file)", "mp4",
            new[] { "-c:v", "libx264", "-crf", "26", "-preset", "medium", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart" }, false, true),
        new MediaPreset("MP4 video, H.264 (high quality)", "mp4",
            new[] { "-c:v", "libx264", "-crf", "19", "-preset", "slow", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart" }, false, true),
        new MediaPreset("WebM video, VP9", "webm",
            new[] { "-c:v", "libvpx-vp9", "-crf", "33", "-b:v", "0", "-row-mt", "1", "-c:a", "libopus", "-b:a", "96k" }, false, true),
        new MediaPreset("Animated GIF (12 frames a second)", "gif", new[] { "-an", "-loop", "0" }, false, true),
        new MediaPreset("Same format, only cut (no re-encoding)", "", new[] { "-c", "copy", "-map", "0" }, false, false),
    };

    public static bool IsMediaFile(string name)
        => VideoFrames.IsVideoFile(name) || AudioExtensions.Contains(Path.GetExtension(name ?? "").TrimStart('.'));

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "aac", "flac", "wav", "ogg", "oga", "opus", "wma", "aiff", "aif", "ape", "alac", "mka", "ac3", "dts",
    };

    /// <summary>Reads "90", "1:30", "01:02:03" or "1:02.5" (hours, minutes, seconds); null when blank or not a time.</summary>
    public static TimeSpan? ParseTime(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;
        var parts = text.Split(':');
        if (parts.Length > 3) return null;
        double total = 0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || v < 0) return null;
            total = total * 60 + v;
        }
        return TimeSpan.FromSeconds(total);
    }

    /// <summary>"1:02:03.500" style, as ffmpeg takes it.</summary>
    public static string FormatTime(TimeSpan t)
        => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";

    /// <summary>
    /// The file the result goes to: the source's name with the preset's type, in
    /// <paramref name="folder"/>, numbered ("clip (2).mp3") rather than ever replacing a file.
    /// </summary>
    public static string TargetFor(string source, string folder, MediaPreset preset, Func<string, bool>? exists = null)
    {
        exists ??= p => File.Exists(p) || Directory.Exists(p);
        string stem = Path.GetFileNameWithoutExtension(source);
        string ext = preset.Extension.Length > 0 ? "." + preset.Extension : Path.GetExtension(source);
        bool sameFolder = string.Equals(Path.GetFullPath(Path.GetDirectoryName(source) ?? "."), Path.GetFullPath(folder),
                                        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        // A cut kept beside its source is marked as one, so the two can be told apart.
        string first = preset.Extension.Length == 0 && sameFolder ? $"{stem} (cut){ext}" : stem + ext;
        string candidate = Path.Combine(folder, first);
        for (int n = 2; exists(candidate) || PathsEqual(candidate, source); n++)
            candidate = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(first)} ({n}){ext}");
        return candidate;
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>The temporary file ffmpeg writes to: hidden, beside the target, ending in the target's own type so ffmpeg picks the format.</summary>
    public static string PartFile(string target)
        => Path.Combine(Path.GetDirectoryName(target)!, "." + Path.GetFileNameWithoutExtension(target) + ".dori-part" + Path.GetExtension(target));

    /// <summary>ffmpeg's arguments for <paramref name="job"/>, writing to <paramref name="output"/>.</summary>
    public static List<string> BuildArgs(MediaJob job, string output)
    {
        var args = new List<string> { "-hide_banner", "-nostdin", "-n" };
        if (job.Start is { } start && start > TimeSpan.Zero) args.AddRange(new[] { "-ss", FormatTime(start) });
        args.AddRange(new[] { "-i", job.Source });
        if (job.End is { } end)
        {
            var length = end - (job.Start ?? TimeSpan.Zero);
            if (length > TimeSpan.Zero) args.AddRange(new[] { "-t", FormatTime(length) });
        }
        args.AddRange(job.Preset.Args);

        var filters = new List<string>();
        if (job.Preset.Extension == "gif")
            filters.Add($"fps=12,scale=-2:{job.Height ?? 360}:flags=lanczos,split[a][b];[a]palettegen[p];[b][p]paletteuse");
        else if (job.Preset.CanResize && job.Height is { } h && h > 0)
            filters.Add($"scale=-2:{h}");
        if (filters.Count > 0) args.AddRange(new[] { job.Preset.Extension == "gif" ? "-filter_complex" : "-vf", string.Join(",", filters) });

        args.AddRange(new[] { "-progress", "pipe:1", "-nostats", output });
        return args;
    }

    private static readonly Regex DurationRx = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    /// <summary>The "Duration: 00:03:12.45," line ffmpeg prints about its input.</summary>
    public static TimeSpan? ParseDuration(string line)
    {
        var m = DurationRx.Match(line ?? "");
        if (!m.Success) return null;
        return TimeSpan.FromHours(int.Parse(m.Groups[1].Value)) + TimeSpan.FromMinutes(int.Parse(m.Groups[2].Value))
               + TimeSpan.FromSeconds(double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>How far ffmpeg has got, from a "-progress" line ("out_time_us=…", "out_time_ms=…" (also microseconds) or "out_time=…").</summary>
    public static TimeSpan? ParseProgress(string line)
    {
        if (string.IsNullOrEmpty(line)) return null;
        int eq = line.IndexOf('=');
        if (eq < 0) return null;
        string key = line[..eq], value = line[(eq + 1)..].Trim();
        if (key is "out_time_us" or "out_time_ms")
            return long.TryParse(value, out long us) && us >= 0 ? TimeSpan.FromTicks(us * 10) : null;
        if (key == "out_time") return ParseTime(value);
        return null;
    }

    /// <summary>
    /// Runs one conversion. <paramref name="progress"/> gets (done, total) in milliseconds of the
    /// result. Throws with ffmpeg's last words when it fails; on cancel ffmpeg is stopped and its
    /// part file removed.
    /// </summary>
    public static void Run(MediaJob job, IProgress<(long Done, long Total)>? progress, CancellationToken ct)
    {
        string ffmpeg = VideoFrames.FfmpegPath ?? throw new InvalidOperationException("ffmpeg isn't installed.");
        if (File.Exists(job.Target)) throw new IOException($"{Path.GetFileName(job.Target)} already exists.");
        string part = PartFile(job.Target);
        try { File.Delete(part); } catch { }

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in BuildArgs(job, part)) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new IOException("ffmpeg didn't start.");
        TimeSpan? total = null;
        if (job.End is { } end) total = end - (job.Start ?? TimeSpan.Zero);
        var lastErrors = new Queue<string>();
        var errDone = new ManualResetEventSlim();
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) { errDone.Set(); return; }
            if (total == null && ParseDuration(e.Data) is { } d) total = d - (job.Start ?? TimeSpan.Zero);
            lock (lastErrors)
            {
                lastErrors.Enqueue(e.Data);
                while (lastErrors.Count > 6) lastErrors.Dequeue();
            }
        };
        p.BeginErrorReadLine();

        using var reg = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { } });
        string? line;
        while ((line = p.StandardOutput.ReadLine()) != null)
        {
            if (ParseProgress(line) is { } done && total is { } t && t > TimeSpan.Zero)
                progress?.Report(((long)Math.Min(done.TotalMilliseconds, t.TotalMilliseconds), (long)t.TotalMilliseconds));
        }
        p.WaitForExit();
        errDone.Wait(2000);

        if (ct.IsCancellationRequested)
        {
            try { File.Delete(part); } catch { }
            ct.ThrowIfCancellationRequested();
        }
        if (p.ExitCode != 0 || !File.Exists(part))
        {
            try { File.Delete(part); } catch { }
            string said;
            lock (lastErrors) said = string.Join("\n", lastErrors.Where(l => l.Trim().Length > 0).TakeLast(3));
            if (said.Contains("does not contain any stream", StringComparison.OrdinalIgnoreCase))
                throw new IOException(job.Preset.AudioOnly
                    ? $"{Path.GetFileName(job.Source)} has no sound in it, so there is nothing to make {job.Preset.Name.Split(' ')[0]} audio from."
                    : $"{Path.GetFileName(job.Source)} has no picture or sound that {job.Preset.Name} can use.");
            throw new IOException($"ffmpeg couldn't convert {Path.GetFileName(job.Source)}" + (said.Length > 0 ? ":\n" + said : "."));
        }
        File.Move(part, job.Target, overwrite: false);
        if (total is { } whole) progress?.Report(((long)whole.TotalMilliseconds, (long)whole.TotalMilliseconds));
    }
}
