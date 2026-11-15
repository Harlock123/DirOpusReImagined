using System;
using System.Collections.Generic;
using System.Linq;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Something that can be undone. <see cref="Description"/> reads after "Undo", e.g. "rename of 3 items".</summary>
    public abstract record UndoEntry(string Description, DateTime When);

    /// <summary>A rename done in one folder: each item's name before and after.</summary>
    public sealed record RenameUndo(string Folder, IReadOnlyList<RenameUndo.Item> Items, DateTime When)
        : UndoEntry(Describe("rename", Items.Count, Items.Count == 1 ? Items[0].NewName : null), When)
    {
        public sealed record Item(string OldName, string NewName, bool IsDirectory);

        internal static string Describe(string verb, int count, string? single)
            => single != null ? $"{verb} of \"{single}\"" : $"{verb} of {count} items";
    }

    /// <summary>
    /// A move out of <paramref name="SourceFolder"/>: each item's original path
    /// (<see cref="TransferItem.Source"/>) and where it went (<see cref="TransferItem.TargetPath"/>).
    /// </summary>
    public sealed record MoveUndo(string SourceFolder, IReadOnlyList<TransferItem> Items, DateTime When)
        : UndoEntry(RenameUndo.Describe("move", Items.Count, Items.Count == 1 ? ConflictResolver.LeafOf(Items[0].TargetPath) : null), When);

    /// <summary>A delete to the trash: each item's original path and where it is in the trash.</summary>
    public sealed record TrashUndo(string Folder, IReadOnlyList<TrashedItem> Items, DateTime When)
        : UndoEntry(RenameUndo.Describe("delete", Items.Count,
                                        Items.Count == 1 ? ConflictResolver.LeafOf(Items[0].OriginalPath) : null), When);

    /// <summary>
    /// The most recent renames, moves and deletes to the trash, newest last. Only the newest can be undone, so undoing
    /// several steps replays them in reverse order.
    /// </summary>
    public sealed class UndoHistory
    {
        public const int MaxEntries = 50;

        private readonly List<UndoEntry> _entries = new();

        /// <summary>Raised whenever an entry is added or taken, so menus can update their text.</summary>
        public event Action? Changed;

        public int Count => _entries.Count;

        /// <summary>The entry the next undo would act on, or null if there's nothing to undo.</summary>
        public UndoEntry? Peek() => _entries.Count == 0 ? null : _entries[^1];

        public void Push(UndoEntry entry)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Changed?.Invoke();
        }

        /// <summary>Removes and returns the newest entry, or null if there is none.</summary>
        public UndoEntry? Pop()
        {
            if (_entries.Count == 0) return null;
            var e = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            Changed?.Invoke();
            return e;
        }
    }

    /// <summary>
    /// Builds undo records from finished operations and turns them back into operations, checking
    /// the disk first: nothing is undone over a file that has since appeared, and items that have
    /// since been moved or deleted are reported rather than guessed at. Existence checks are passed
    /// in so this is testable; <see cref="Exists"/> is the real one.
    /// </summary>
    public static class UndoPlanner
    {
        /// <summary>True if a file or folder exists at <paramref name="path"/> (any provider).</summary>
        public static bool Exists(string path)
        {
            try
            {
                var p = ProviderRegistry.For(path);
                return p.FileExists(path) || p.DirectoryExists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The undo record for a finished (or partly finished, or cancelled) move. Only items that
        /// actually left their source and arrived are included. Items that replaced something at the
        /// destination (<paramref name="overwrittenTargets"/>) are left out: moving them back would
        /// take the merged-in contents along, and the replaced file can't be brought back anyway.
        /// Returns null if nothing can be undone.
        /// </summary>
        public static MoveUndo? RecordMove(string sourceFolder, IReadOnlyList<TransferItem> items, ISet<string> overwrittenTargets,
                                           Func<string, bool> exists, DateTime now)
        {
            var done = items
                .Where(i => !PathEquals(i.Source, i.TargetPath))
                .Where(i => !overwrittenTargets.Contains(i.TargetPath))
                .Where(i => exists(i.TargetPath) && !exists(i.Source))
                .ToList();
            return done.Count == 0 ? null : new MoveUndo(sourceFolder, done, now);
        }

        /// <summary>The undo record for a rename plan's successful rows, or null if none succeeded.</summary>
        public static RenameUndo? RecordRename(string folder, IEnumerable<RenameUndo.Item> succeeded, DateTime now)
        {
            var items = succeeded.ToList();
            return items.Count == 0 ? null : new RenameUndo(folder, items, now);
        }

        /// <summary>
        /// The moves that put each item back where it came from, and a note for each item that
        /// can't be: it's no longer where it was moved to, or something new now sits at its old path.
        /// </summary>
        public static (List<TransferItem> Moves, List<string> Problems) ReverseMove(MoveUndo undo, Func<string, bool> exists)
        {
            var moves = new List<TransferItem>();
            var problems = new List<string>();
            foreach (var i in undo.Items)
            {
                string name = ConflictResolver.LeafOf(i.TargetPath);
                if (!exists(i.TargetPath))
                {
                    problems.Add($"{name}: no longer at {i.TargetPath}");
                    continue;
                }
                if (exists(i.Source))
                {
                    problems.Add($"{ConflictResolver.LeafOf(i.Source)}: something new is already at {i.Source}");
                    continue;
                }
                moves.Add(new TransferItem(i.TargetPath, undo.SourceFolder, i.Source, i.IsDirectory));
            }
            return (moves, problems);
        }

        /// <summary>
        /// A rename plan that gives each item its old name back, and a note for each item that
        /// can't be renamed back. The plan is applied with the same two-step rename as any other,
        /// so swaps and chains (a→b, b→c) reverse cleanly.
        /// </summary>
        public static (List<RenamePlanRow> Plan, List<string> Problems) ReverseRename(
            RenameUndo undo, Func<string, bool> exists, Func<string, string, string> join)
        {
            var plan = new List<RenamePlanRow>();
            var problems = new List<string>();
            var restoring = new HashSet<string>(undo.Items.Select(i => i.NewName), StringComparer.Ordinal);

            foreach (var i in undo.Items)
            {
                if (!exists(join(undo.Folder, i.NewName)))
                {
                    problems.Add($"{i.NewName}: no longer in {undo.Folder}");
                    continue;
                }
                // The old name must be free, unless it's held by another item being renamed back
                // (a swap or chain), or this is a change of case only, which "exists" can't tell apart.
                bool caseOnly = string.Equals(i.OldName, i.NewName, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && !restoring.Contains(i.OldName) && exists(join(undo.Folder, i.OldName)))
                {
                    problems.Add($"{i.OldName}: something new already has that name");
                    continue;
                }
                plan.Add(new RenamePlanRow(i.NewName, i.OldName, i.IsDirectory) { Status = RenameStatus.Change });
            }
            return (plan, problems);
        }

        /// <summary>The undo record for a delete, or null if nothing went to the trash (a permanent delete can't be undone).</summary>
        public static TrashUndo? RecordTrash(string folder, IReadOnlyList<TrashedItem> trashed, DateTime now)
            => trashed.Count == 0 ? null : new TrashUndo(folder, trashed, now);

        /// <summary>
        /// The trashed items that can be put back, and a note for each that can't: it's no longer
        /// in the trash, or something new now sits at its old path.
        /// </summary>
        public static (List<TrashedItem> Restores, List<string> Problems) ReverseTrash(TrashUndo undo, Func<string, bool> exists)
        {
            var restores = new List<TrashedItem>();
            var problems = new List<string>();
            foreach (var i in undo.Items)
            {
                string name = ConflictResolver.LeafOf(i.OriginalPath);
                if (!exists(i.TrashedPath)) problems.Add($"{name}: no longer in the trash");
                else if (exists(i.OriginalPath)) problems.Add($"{name}: something new is already at {i.OriginalPath}");
                else restores.Add(i);
            }
            return (restores, problems);
        }

        private static bool PathEquals(string a, string b)
            => string.Equals(a.TrimEnd('/', '\\'), b.TrimEnd('/', '\\'),
                             OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }
}
