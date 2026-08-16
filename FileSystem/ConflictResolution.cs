using System;
using System.Collections.Generic;
using System.IO;

namespace DirOpusReImagined.FileSystem;

/// <summary>What to do with one item whose target already exists at the destination.</summary>
public enum ConflictResolution
{
    /// <summary>Don't transfer this item; leave the existing destination untouched.</summary>
    Skip,

    /// <summary>Replace the destination (for a folder, merge into it — the existing behavior).</summary>
    Overwrite,

    /// <summary>Transfer under a new, non-colliding name (see <see cref="ConflictDecision.NewName"/>).</summary>
    Rename
}

/// <summary>A per-item decision. <see cref="NewName"/> is only used when <see cref="Resolution"/> is Rename.</summary>
public readonly record struct ConflictDecision(ConflictResolution Resolution, string? NewName);

/// <summary>
/// Pure helpers for resolving copy/move name collisions: suggesting a "Keep both" name and rewriting
/// a batch of <see cref="TransferItem"/>s according to per-item decisions. No I/O — callers pass in
/// predicates for existence checks so these stay unit-testable.
/// </summary>
public static class ConflictResolver
{
    /// <summary>
    /// Suggests a non-colliding "Keep both" name for <paramref name="originalName"/> by appending
    /// " (2)", " (3)", … before the extension (files) or to the whole name (directories), skipping any
    /// candidate for which <paramref name="nameExists"/> returns true.
    /// </summary>
    public static string SuggestKeepBothName(string originalName, bool isDirectory, Func<string, bool> nameExists)
    {
        string stem = isDirectory ? originalName : Path.GetFileNameWithoutExtension(originalName);
        string ext = isDirectory ? "" : Path.GetExtension(originalName); // includes the leading dot, or ""

        // Dotfiles like ".env" / ".gitignore" split as (stem="", ext=".env"), which would give
        // " (2).env" — treat the whole name as the stem instead.
        if (string.IsNullOrEmpty(stem)) { stem = originalName; ext = ""; }

        for (int n = 2; n < 100000; n++)
        {
            string candidate = $"{stem} ({n}){ext}";
            if (!nameExists(candidate)) return candidate;
        }
        // Pathological fallback — should never be reached in practice.
        return $"{stem} (copy){ext}";
    }

    /// <summary>
    /// Produces the final transfer list from <paramref name="items"/> given per-source decisions.
    /// Items whose <c>Source</c> is not in <paramref name="decisionsBySource"/> pass through unchanged.
    /// <list type="bullet">
    /// <item><b>Skip</b> — the item is omitted.</item>
    /// <item><b>Overwrite</b> — the item is kept as-is (the transfer engine overwrites).</item>
    /// <item><b>Rename</b> — the item's target leaf is replaced with the decision's NewName.</item>
    /// </list>
    /// </summary>
    public static List<TransferItem> ApplyResolutions(
        IReadOnlyList<TransferItem> items,
        IReadOnlyDictionary<string, ConflictDecision> decisionsBySource)
    {
        var result = new List<TransferItem>(items.Count);
        foreach (var item in items)
        {
            if (!decisionsBySource.TryGetValue(item.Source, out var decision))
            {
                result.Add(item);
                continue;
            }

            switch (decision.Resolution)
            {
                case ConflictResolution.Skip:
                    break; // drop it

                case ConflictResolution.Overwrite:
                    result.Add(item);
                    break;

                case ConflictResolution.Rename:
                    string newName = string.IsNullOrWhiteSpace(decision.NewName)
                        ? LeafOf(item.TargetPath)
                        : decision.NewName!;
                    result.Add(item with { TargetPath = ReplaceLeaf(item.TargetPath, newName) });
                    break;
            }
        }
        return result;
    }

    /// <summary>The final path segment (file/folder name), ignoring any trailing separators.</summary>
    public static string LeafOf(string fullPath)
    {
        string trimmed = fullPath.TrimEnd('/', '\\');
        int i = trimmed.LastIndexOfAny(Separators);
        return i < 0 ? trimmed : trimmed.Substring(i + 1);
    }

    /// <summary>Replaces the final path segment of <paramref name="fullPath"/> with <paramref name="newLeaf"/>,
    /// preserving the original separator style (works for local, UNC, and cloud:// paths).</summary>
    public static string ReplaceLeaf(string fullPath, string newLeaf)
    {
        string trimmed = fullPath.TrimEnd('/', '\\');
        int i = trimmed.LastIndexOfAny(Separators);
        return i < 0 ? newLeaf : trimmed.Substring(0, i + 1) + newLeaf;
    }

    private static readonly char[] Separators = { '/', '\\' };
}
