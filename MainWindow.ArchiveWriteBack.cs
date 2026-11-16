using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>
    /// Saving edits back into archives. A file opened from inside an archive (double-click, or a
    /// button's %AF%-style token) is a temporary copy; when another program saves it, DORI asks
    /// whether to put the changed file back into the archive. Archives DORI can't write (7z, RAR)
    /// get a note saying so instead.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ArchiveWriteBack _writeBack = new();
        private FileSystemWatcher? _toolTempWatcher;
        private readonly Dictionary<string, DispatcherTimer> _writeBackTimers = new();
        private readonly HashSet<string> _writeBackAsking = new();

        private void InitArchiveWriteBack()
        {
            Closing += (_, _) => { _toolTempWatcher?.Dispose(); _toolTempWatcher = null; };
        }

        /// <summary>Called after an archive entry is extracted to <paramref name="tempPath"/>.</summary>
        private void TrackArchiveCopy(string entryUri, string tempPath)
        {
            try
            {
                var ap = ArchivePath.Parse(entryUri);
                _writeBack.Track(tempPath, ap.ArchiveFsPath, ap.EntryPath);
                EnsureToolTempWatcher();
            }
            catch (Exception ex) { CrashLog.Write("Archive write-back", ex); }
        }

        private void EnsureToolTempWatcher()
        {
            if (_toolTempWatcher != null || _toolTempDir == null || !Directory.Exists(_toolTempDir)) return;
            var w = new FileSystemWatcher(_toolTempDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            // Editors save by writing in place, or by writing a new file and renaming it over the old.
            w.Changed += (_, e) => OnToolTempChanged(e.FullPath);
            w.Created += (_, e) => OnToolTempChanged(e.FullPath);
            w.Renamed += (_, e) => OnToolTempChanged(e.FullPath);
            w.EnableRaisingEvents = true;
            _toolTempWatcher = w;
        }

        /// <summary>Waits until the file has been quiet for a second (a save is often several writes), then checks it.</summary>
        private void OnToolTempChanged(string path)
        {
            if (_writeBack.Find(path) == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_writeBackTimers.TryGetValue(path, out var timer))
                {
                    timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    timer.Tick += (_, _) => { timer.Stop(); _ = OfferWriteBackAsync(path); };
                    _writeBackTimers[path] = timer;
                }
                timer.Stop();
                timer.Start();
            });
        }

        private async Task OfferWriteBackAsync(string path)
        {
            if (_writeBackAsking.Contains(path)) return;   // asking already; the answer will see the latest save
            var changed = _writeBack.Changed(path);
            if (changed == null) return;

            _writeBackAsking.Add(path);
            try
            {
                if (ArchiveEditor.WhyReadOnly(changed.ArchiveFsPath) is { } why)
                {
                    _writeBack.Accept(changed);
                    await new MessageBox($"{changed.Name} was changed, but it can't be put back into {changed.ArchiveName}. {why}\n\n" +
                                         $"The changed copy is here until DORI closes:\n{changed.TempPath}",
                                         "Changed file from an archive").ShowDialog(this);
                    return;
                }

                bool put = await new MessageBox($"{changed.Name} was changed since it was taken out of {changed.ArchiveName}.\n\n" +
                                                $"Put the changed file back into {changed.ArchiveName}?",
                                                true, "Update the Archive", "Not Now", "Changed file from an archive").ShowDialog<bool>(this);
                if (!put)
                {
                    _writeBack.Accept(changed);   // asked again only after the next save
                    return;
                }
                try
                {
                    await Task.Run(() => _writeBack.WriteBack(changed));
                    RefreshPanelsShowing(ArchivePath.RootUriFor(changed.ArchiveFsPath));
                    RefreshPanelsShowing(Path.GetDirectoryName(changed.ArchiveFsPath) ?? "");
                }
                catch (Exception ex)
                {
                    await new MessageBox($"{changed.Name} couldn't be put back into {changed.ArchiveName}: {ex.Message}",
                                         "Changed file from an archive").ShowDialog(this);
                }
            }
            finally { _writeBackAsking.Remove(path); }
        }
    }
}
