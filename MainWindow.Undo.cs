using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Undo for renames and moves (Ctrl/Cmd+Z, or right-click → Undo). Finished renames and moves
    /// are recorded in <see cref="_undo"/>; undoing the newest one checks the disk first and then
    /// renames or moves the items back through the normal rename and transfer code.
    /// </summary>
    public partial class MainWindow
    {
        private readonly UndoHistory _undo = new();
        private bool _undoRunning;

        private void InitUndo()
        {
            _undo.Changed += UpdateUndoMenus;
            UpdateUndoMenus();
        }

        private void UpdateUndoMenus()
        {
            string? description = _undo.Peek()?.Description;
            LPgrid.SetUndoDescription(description);
            RPgrid.SetUndoDescription(description);
        }

        /// <summary>
        /// After a move finishes (fully, partly, or cancelled), records the items that really moved.
        /// Runs the existence checks off the UI thread, since cloud paths make network calls.
        /// </summary>
        private async void RecordMoveForUndo(string sourceFolder, IReadOnlyList<TransferItem> items,
                                             ISet<string> overwrittenTargets)
        {
            try
            {
                var record = await Task.Run(() =>
                    UndoPlanner.RecordMove(sourceFolder, items, overwrittenTargets, UndoPlanner.Exists, DateTime.Now));
                if (record != null) _undo.Push(record);
            }
            catch (Exception ex)
            {
                CrashLog.Write("Undo record", ex);
            }
        }

        /// <summary>Undoes the newest rename or move.</summary>
        private async void UndoLast()
        {
            if (_undoRunning) return;
            var entry = _undo.Peek();
            if (entry == null)
            {
                await new MessageBox("There is nothing to undo.", "Undo").ShowDialog(this);
                return;
            }

            _undoRunning = true;
            try
            {
                switch (entry)
                {
                    case RenameUndo rename:
                        await UndoRenameAsync(rename);
                        break;
                    case MoveUndo move:
                        await UndoMoveAsync(move);
                        break;
                }
            }
            catch (Exception ex)
            {
                CrashLog.Write("Undo", ex);
                await new MessageBox("Undo failed: " + ex.Message, "Undo").ShowDialog(this);
            }
            finally
            {
                _undoRunning = false;
            }
        }

        private async Task UndoRenameAsync(RenameUndo rename)
        {
            var (plan, problems) = await Task.Run(() =>
                UndoPlanner.ReverseRename(rename, UndoPlanner.Exists, FileUtility.JoinPanelPath));
            _undo.Pop();

            if (plan.Count > 0)
            {
                var (errors, _) = await FileUtility.ApplyRenamePlanDetailedAsync(rename.Folder, plan);
                problems.AddRange(errors);
            }

            RefreshPanelsShowing(rename.Folder);
            await ReportUndoProblems("renamed back", problems);
        }

        private async Task UndoMoveAsync(MoveUndo move)
        {
            // Moving files can take a while (between drives, or to and from the cloud), so ask first.
            string what = move.Items.Count == 1
                ? $"\"{ConflictResolver.LeafOf(move.Items[0].TargetPath)}\""
                : $"{move.Items.Count} items";
            string folder = move.SourceFolder.TrimEnd('/', '\\');
            var confirm = new MessageBox($"Move {what} back to {folder}?", showCancel: true,
                                         okText: "Move back", cancelText: "Cancel", title: "Undo move");
            if (!await confirm.ShowDialog<bool>(this)) return;

            var (moves, problems) = await Task.Run(() => UndoPlanner.ReverseMove(move, UndoPlanner.Exists));
            _undo.Pop();

            if (moves.Count > 0)
            {
                string title = $"Undo: moving {(moves.Count == 1 ? "1 item" : $"{moves.Count} items")} back → {folder}";
                var op = FileOperation.NeedsArchiveTransfer(true, moves)
                    ? FileOperation.ArchiveTransfer(true, moves, title, move.SourceFolder)
                    : FileOperation.Transfer(true, moves, title, move.SourceFolder);
                op.AfterFinish = done => Dispatcher.UIThread.Post(() =>
                {
                    RefreshPanel(isLeft: true, manual: true);
                    RefreshPanel(isLeft: false, manual: true);
                    if (done.Status == OperationStatus.Failed)
                        OperationsWindow.ShowSingleton(this);
                });
                OperationQueue.Instance.Enqueue(op);
                OperationsWindow.ShowSingleton(this);
            }

            await ReportUndoProblems("moved back", problems);
        }

        private async Task ReportUndoProblems(string verb, List<string> problems)
        {
            if (problems.Count == 0) return;
            const int maxShown = 10;
            string detail = string.Join("\n", problems.Take(maxShown));
            if (problems.Count > maxShown) detail += $"\n… and {problems.Count - maxShown} more.";
            await new MessageBox($"{problems.Count} item(s) couldn't be {verb}:\n\n{detail}", "Undo").ShowDialog(this);
        }

        private void RefreshPanelsShowing(string folder)
        {
            if (PathsEqual(LPpath.Text ?? "", folder)) RefreshPanel(isLeft: true, manual: true);
            else if (PathsEqual(RPpath.Text ?? "", folder)) RefreshPanel(isLeft: false, manual: true);
        }
    }
}
