using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Colour labels and tags (right-click → Label). Labels live in <see cref="LabelStore"/>; each
    /// listing copies them onto its rows (the row tint and the Tags column), and the filter box
    /// matches them with <c>tag:name</c>.
    /// </summary>
    public partial class MainWindow
    {
        private DispatcherTimer? _labelSaveTimer;

        private void InitLabels()
        {
            LPgrid.LabelColourRequested += (_, c) => SetLabelColour(isLeft: true, c);
            RPgrid.LabelColourRequested += (_, c) => SetLabelColour(isLeft: false, c);
            LabelStore.Current.Changed += OnLabelsChanged;
            Closing += (_, _) => SaveLabels();
        }

        /// <summary>The panel's folder and the rows to act on: the selection, or the row under the cursor.</summary>
        private (string Folder, List<AFileEntry> Entries) Picked(bool isLeft)
        {
            var grid = isLeft ? LPgrid : RPgrid;
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            var picked = grid.SelectedItems.OfType<AFileEntry>().ToList();
            if (picked.Count == 0 && grid.CursorItem is AFileEntry cursor) picked.Add(cursor);
            return (folder, picked.Where(f => !string.IsNullOrEmpty(f.Name) && f.Name != "..").ToList());
        }

        /// <summary>Full paths of the picked rows, or null (after saying why) when labels can't be used there.</summary>
        private List<string>? LabelTargets(bool isLeft)
        {
            var (folder, entries) = Picked(isLeft);
            if (string.IsNullOrEmpty(folder)) return null;
            if (ProviderRegistry.For(folder) is not LocalFileProvider)
            {
                _ = new MessageBox("Labels and tags are for files and folders on this computer, " +
                                   "not in cloud storage or inside archives.", "Labels").ShowDialog(this);
                return null;
            }
            if (entries.Count == 0)
            {
                _ = new MessageBox("Select the files or folders to label first.", "Labels").ShowDialog(this);
                return null;
            }
            return entries.Select(e => FileUtility.JoinPanelPath(folder, e.Name)).ToList();
        }

        private void SetLabelColour(bool isLeft, LabelColour colour)
        {
            if (LabelTargets(isLeft) is { } paths) LabelStore.Current.SetColour(paths, colour);
        }

        private async void EditLabels(bool isLeft)
        {
            if (LabelTargets(isLeft) is not { } paths) return;
            var store = LabelStore.Current;
            var current = paths.Select(p => store.Get(p).Tags).ToList();
            // Show the tags they all share; with nothing shared, start empty.
            var shared = current.Skip(1).Aggregate(current[0].AsEnumerable(),
                (acc, t) => acc.Intersect(t, StringComparer.OrdinalIgnoreCase)).ToList();
            var known = store.AllTags();
            string prompt = (paths.Count == 1 ? $"Tags for {Path.GetFileName(paths[0])}" : $"Tags for {paths.Count} items") +
                            ", separated by commas. Leave it empty to remove them." +
                            (known.Count > 0 ? $"\nIn use: {string.Join(", ", known.Take(12))}{(known.Count > 12 ? ", …" : "")}" : "") +
                            "\nFilter a panel by a tag or colour by typing tag:name in its filter box.";
            var dialog = new TextPromptDialog("Tags", prompt, string.Join(", ", shared)) { AllowEmpty = true };
            string? text = await dialog.ShowDialog<string?>(this);
            if (text == null) return;
            store.SetTags(paths, LabelStore.ParseTags(text));
        }

        private void OnLabelsChanged()
        {
            ApplyLabels(LPgrid, LPpath.Text);
            ApplyLabels(RPgrid, RPpath.Text);
            // Save a moment later, so a burst of changes writes the file once.
            _labelSaveTimer ??= new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                _labelSaveTimer!.Stop();
                SaveLabels();
            });
            _labelSaveTimer.Stop();
            _labelSaveTimer.Start();
        }

        private static void SaveLabels()
        {
            try { LabelStore.Current.Save(); }
            catch (Exception ex) { CrashLog.Write("Saving labels", ex); }
        }

        /// <summary>Copies the labels onto a panel's rows, including rows hidden by its filter.</summary>
        private void ApplyLabels(TaiDataGrid grid, string? folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            var rows = (ReferenceEquals(grid, LPgrid) ? _lpUnfilteredItems : _rpUnfilteredItems)
                       .Concat(grid.Items).OfType<AFileEntry>().Distinct();
            bool local = ProviderRegistry.For(folder) is LocalFileProvider;
            var store = LabelStore.Current;
            foreach (var row in rows)
            {
                var label = local && store.Count > 0 && row.Name != ".." ? store.Get(FileUtility.JoinPanelPath(folder, row.Name)) : FileLabel.Empty;
                row.Label = label;
                row.LabelColour = label.Colour;
                row.Tags = label.Display;
            }
            grid.RefreshOptionalColumns();
        }

        /// <summary>Keeps labels with their items when DORI renames or moves them.</summary>
        private void PushUndo(UndoEntry entry)
        {
            _undo.Push(entry);
            try
            {
                var store = LabelStore.Current;
                if (store.Count == 0) return;
                switch (entry)
                {
                    case RenameUndo r:
                        foreach (var item in r.Items)
                            store.Move(FileUtility.JoinPanelPath(r.Folder, item.OldName), FileUtility.JoinPanelPath(r.Folder, item.NewName));
                        break;
                    case MoveUndo m:
                        foreach (var item in m.Items)
                            if (ProviderRegistry.For(item.TargetPath) is LocalFileProvider)
                                store.Move(item.Source, item.TargetPath);
                        break;
                }
            }
            catch (Exception ex) { CrashLog.Write("Moving labels", ex); }
        }
    }
}
