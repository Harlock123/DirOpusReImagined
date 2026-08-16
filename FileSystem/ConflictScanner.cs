using System;
using System.Collections.Generic;

namespace DirOpusReImagined.FileSystem;

/// <summary>Details of one item whose target already exists at the destination, with source and
/// destination metadata so the user can decide informedly.</summary>
public sealed class ConflictInfo
{
    public required TransferItem Item { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }

    public bool HasSourceStat { get; init; }
    public long SourceSize { get; init; }
    public DateTime SourceModified { get; init; }

    public bool HasDestStat { get; init; }
    public long DestSize { get; init; }
    public DateTime DestModified { get; init; }

    /// <summary>A pre-computed non-colliding "Keep both" name, unique across this batch.</summary>
    public required string SuggestedNewName { get; init; }
}

/// <summary>Finds which top-level transfer items already exist at their destination. Does blocking
/// existence/stat calls (network for cloud/UNC), so callers run it off the UI thread.</summary>
public static class ConflictScanner
{
    public static List<ConflictInfo> Scan(IReadOnlyList<TransferItem> items)
    {
        var conflicts = new List<ConflictInfo>();
        // Track names a "Keep both" suggestion must avoid: the names of every item in the batch
        // (so a suggestion never collides with something else being transferred) plus the suggestions
        // we've already handed out.
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items) planned.Add(ConflictResolver.LeafOf(it.TargetPath));

        foreach (var it in items)
        {
            try
            {
                var dstP = ProviderRegistry.For(it.TargetPath);
                bool exists = it.IsDirectory
                    ? dstP.DirectoryExists(it.TargetPath)
                    : dstP.FileExists(it.TargetPath);
                if (!exists) continue;

                var srcP = ProviderRegistry.For(it.Source);
                var s = TryStat(srcP, it.Source);
                var d = TryStat(dstP, it.TargetPath);

                string name = ConflictResolver.LeafOf(it.Source);
                string suggestion = ConflictResolver.SuggestKeepBothName(name, it.IsDirectory, cand =>
                    planned.Contains(cand) || TargetExists(dstP, it.TargetPath, cand, it.IsDirectory));
                planned.Add(suggestion);

                conflicts.Add(new ConflictInfo
                {
                    Item = it,
                    Name = name,
                    IsDirectory = it.IsDirectory,
                    HasSourceStat = s != null,
                    SourceSize = s?.Size ?? -1,
                    SourceModified = s?.LastModified ?? default,
                    HasDestStat = d != null,
                    DestSize = d?.Size ?? -1,
                    DestModified = d?.LastModified ?? default,
                    SuggestedNewName = suggestion,
                });
            }
            catch
            {
                // If we can't determine existence, don't treat it as a conflict (the transfer's own
                // overwrite:true handles it). Blocking a valid transfer on a flaky probe is worse.
            }
        }
        return conflicts;
    }

    private static FileEntry? TryStat(IFileProvider provider, string path)
    {
        try { return provider.Stat(path); }
        catch { return null; }
    }

    private static bool TargetExists(IFileProvider dstP, string existingTargetPath, string candidateName, bool isDir)
    {
        try
        {
            string candidatePath = ConflictResolver.ReplaceLeaf(existingTargetPath, candidateName);
            return isDir ? dstP.DirectoryExists(candidatePath) : dstP.FileExists(candidatePath);
        }
        catch { return false; }
    }
}
