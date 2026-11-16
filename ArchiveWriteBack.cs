using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>
    /// Keeps track of files taken out of archives into temporary copies for other programs, so
    /// that when one is saved, the change can be put back into the archive. A copy counts as
    /// changed when its contents differ from what was last taken out or put back (an editor that
    /// only touches the file doesn't count).
    /// </summary>
    public sealed class ArchiveWriteBack
    {
        /// <summary>One temporary copy and the archive entry it came from.</summary>
        public sealed class Tracked
        {
            public string TempPath { get; init; } = "";
            public string ArchiveFsPath { get; init; } = "";
            public string Entry { get; init; } = "";
            internal string Hash = "";

            public string Name => Path.GetFileName(Entry.TrimEnd('/'));
            public string ArchiveName => Path.GetFileName(ArchiveFsPath);
        }

        private readonly Dictionary<string, Tracked> _byTemp = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        /// <summary>Starts watching <paramref name="tempPath"/>, just written from <paramref name="entry"/> in the archive.</summary>
        public Tracked Track(string tempPath, string archiveFsPath, string entry)
        {
            var t = new Tracked { TempPath = Path.GetFullPath(tempPath), ArchiveFsPath = archiveFsPath, Entry = entry, Hash = HashOf(tempPath) ?? "" };
            lock (_gate) _byTemp[t.TempPath] = t;
            return t;
        }

        public Tracked? Find(string tempPath)
        {
            lock (_gate) return _byTemp.TryGetValue(Path.GetFullPath(tempPath), out var t) ? t : null;
        }

        /// <summary>The tracked copy at <paramref name="tempPath"/> if its contents changed since last time, else null.</summary>
        public Tracked? Changed(string tempPath)
        {
            var t = Find(tempPath);
            if (t == null) return null;
            string? now = HashOf(t.TempPath);
            return now != null && now != t.Hash ? t : null;
        }

        /// <summary>Takes the copy's current contents as the ones to compare with, so it isn't reported again until it changes again.</summary>
        public void Accept(Tracked t)
        {
            string? now = HashOf(t.TempPath);
            if (now != null) t.Hash = now;
        }

        /// <summary>Puts the copy back into its archive in place of the entry (one rewrite of the archive), then accepts it.</summary>
        public void WriteBack(Tracked t, CancellationToken ct = default)
        {
            if (ArchiveEditor.WhyReadOnly(t.ArchiveFsPath) is { } why) throw new NotSupportedException(why);
            var edit = new ArchiveEdit();
            edit.Adds.Add(new ArchiveAdd(t.Entry, t.TempPath, false, File.GetLastWriteTime(t.TempPath)));
            ArchiveEditor.Apply(t.ArchiveFsPath, edit, null, ct);
            Accept(t);
        }

        private static string? HashOf(string path)
        {
            // An editor may still be writing; a failed read just means "look again later".
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexString(SHA256.HashData(stream));
            }
            catch { return null; }
        }
    }
}
