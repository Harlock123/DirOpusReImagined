namespace DirOpusReImagined.Tests;

/// <summary>A fresh, uniquely named temp directory that is deleted (recursively) on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dori-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>Joins relative segments ('/'-separated) onto the temp root using the platform separator.</summary>
    public string Combine(string relative)
        => System.IO.Path.Combine(new[] { Path }.Concat(relative.Split('/')).ToArray());

    /// <summary>Creates a sub-directory (and its parents) and returns its full path.</summary>
    public string Dir(string relative)
    {
        var p = Combine(relative);
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Writes a file (creating parent folders), optionally stamping its UTC modification time.</summary>
    public string File(string relative, string content = "", DateTime? mtimeUtc = null)
    {
        var p = Combine(relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        if (mtimeUtc is { } t) System.IO.File.SetLastWriteTimeUtc(p, t);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>Shared fixed timestamps (whole seconds, far apart) so tests don't depend on fs resolution.</summary>
public static class Times
{
    public static readonly DateTime Base = new(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Later = Base.AddMinutes(10);
    public static readonly DateTime Earlier = Base.AddMinutes(-10);
}
