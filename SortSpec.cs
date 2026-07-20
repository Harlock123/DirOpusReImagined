namespace DirOpusReImagined;

/// <summary>Which column a panel's file list is ordered by.</summary>
public enum SortKey
{
    Name,
    Size,
    Date,
    Type
}

/// <summary>A panel's sort order: the key plus direction. Applies to files; folders stay grouped
/// first and alphabetical.</summary>
public sealed record SortSpec(SortKey Key, bool Ascending)
{
    public static readonly SortSpec Default = new(SortKey.Name, true);

    /// <summary>Toggles direction (same key) or switches to <paramref name="key"/> with a sensible
    /// default direction (Name ascending; Size/Date/Type descending, i.e. biggest/newest first).</summary>
    public SortSpec Toward(SortKey key)
        => key == Key ? this with { Ascending = !Ascending }
                      : new SortSpec(key, key == SortKey.Name);
}
