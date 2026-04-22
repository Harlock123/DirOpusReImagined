using System;

namespace DirOpusReImagined.FileSystem.Rclone;

public readonly record struct CloudPath(string Remote, string Path)
{
    public const string Scheme = "cloud://";

    public string Fs => Remote + ":";

    public string FullUri => string.IsNullOrEmpty(Path)
        ? $"{Scheme}{Remote}/"
        : $"{Scheme}{Remote}/{Path}";

    public CloudPath WithPath(string newPath) => new(Remote, newPath);

    public CloudPath Join(string child)
    {
        var trimmed = child.TrimStart('/');
        var combined = string.IsNullOrEmpty(Path) ? trimmed : $"{Path.TrimEnd('/')}/{trimmed}";
        return new CloudPath(Remote, combined);
    }

    public static bool IsCloudUri(string uri)
        => !string.IsNullOrEmpty(uri)
           && uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);

    public static CloudPath Parse(string uri)
    {
        if (!IsCloudUri(uri))
            throw new ArgumentException($"Not a cloud URI: {uri}", nameof(uri));

        var rest = uri.Substring(Scheme.Length);
        var slash = rest.IndexOf('/');
        if (slash < 0) return new CloudPath(rest, "");

        var remote = rest.Substring(0, slash);
        var path = rest.Substring(slash + 1).TrimEnd('/');
        return new CloudPath(remote, path);
    }
}
