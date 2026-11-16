using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The Git column: inside a git work tree each listing asks git for the status of what it
    /// shows, off the UI thread, and fills the column when the answer comes.
    /// </summary>
    public partial class MainWindow
    {
        private CancellationTokenSource? _lpGitCts, _rpGitCts;

        private async void RequestGitStatus(TaiDataGrid grid, string? folder)
        {
            bool left = ReferenceEquals(grid, LPgrid);
            (left ? _lpGitCts : _rpGitCts)?.Cancel();
            if (string.IsNullOrEmpty(folder) || ProviderRegistry.For(folder) is not LocalFileProvider) return;
            var cts = new CancellationTokenSource();
            if (left) _lpGitCts = cts; else _rpGitCts = cts;

            var rows = (left ? _lpUnfilteredItems : _rpUnfilteredItems).Concat(grid.Items)
                       .OfType<AFileEntry>().Where(r => r.Name != "..").Distinct().ToList();
            var names = rows.Select(r => r.Name).ToList();
            Dictionary<string, string>? words;
            try
            {
                words = await System.Threading.Tasks.Task.Run(() => GitStatus.ForFolderAsync(folder, names, cts.Token), cts.Token);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { CrashLog.Write("Git status", ex); return; }
            if (cts.IsCancellationRequested) return;

            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested) return;
                bool changed = false;
                foreach (var row in rows)
                {
                    string word = words != null && words.TryGetValue(row.Name, out var w) ? w : "";
                    if (row.Git != word) { row.Git = word; changed = true; }
                }
                if (changed) grid.RefreshOptionalColumns();
            });
        }
    }
}
