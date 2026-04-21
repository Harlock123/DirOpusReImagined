using System.Collections.Generic;

namespace DirOpusReImagined.FileSystem;

public static class ProviderRegistry
{
    private static readonly LocalFileProvider _local = new();
    private static readonly List<IFileProvider> _extra = new();

    public static void Register(IFileProvider provider) => _extra.Insert(0, provider);

    public static IFileProvider For(string path)
    {
        foreach (var p in _extra)
            if (p.CanHandle(path)) return p;
        return _local;
    }

    public static IFileProvider Local => _local;
}
