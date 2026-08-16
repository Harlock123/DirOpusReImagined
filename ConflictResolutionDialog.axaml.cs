using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Modal dialog shown before a Copy/Move when some selected items already exist at the
    /// destination. Offers a per-item choice of Skip / Overwrite / Keep both (rename), with
    /// bulk "apply to all" buttons, and returns the decisions keyed by source path.
    /// </summary>
    public partial class ConflictResolutionDialog : Window
    {
        private readonly List<Row> _rows = new();

        /// <summary>Per-source decisions, populated when the user clicks Apply.</summary>
        public Dictionary<string, ConflictDecision> Decisions { get; } = new();

        private sealed class Row
        {
            public ConflictInfo Info = null!;
            public RadioButton Skip = null!;
            public RadioButton Overwrite = null!;
            public RadioButton KeepBoth = null!;
            public TextBox NewName = null!;
        }

        // Designer ctor.
        public ConflictResolutionDialog()
        {
            InitializeComponent();
        }

        public ConflictResolutionDialog(IReadOnlyList<ConflictInfo> conflicts)
        {
            InitializeComponent();

            HeaderText.Text = conflicts.Count == 1
                ? "1 item already exists at the destination. Choose what to do with it."
                : $"{conflicts.Count} items already exist at the destination. Choose what to do with each.";

            for (int i = 0; i < conflicts.Count; i++)
                RowsPanel.Children.Add(BuildRow(conflicts[i], i));
        }

        private Control BuildRow(ConflictInfo info, int index)
        {
            var title = new TextBlock
            {
                FontWeight = FontWeight.SemiBold,
                Text = info.Name + (info.IsDirectory ? "   (folder)" : "")
            };

            var meta = new TextBlock
            {
                Foreground = Brushes.Gray,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Text = BuildMeta(info)
            };

            // Per-row radio group (unique GroupName so rows are independent).
            string group = "conflict_" + index;
            var skip = new RadioButton { GroupName = group, Content = "Skip" };
            var overwrite = new RadioButton { GroupName = group, Content = "Overwrite", IsChecked = true };
            var keepBoth = new RadioButton { GroupName = group, Content = "Keep both" };

            var choices = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 14,
                Margin = new Thickness(0, 4, 0, 0)
            };
            choices.Children.Add(skip);
            choices.Children.Add(overwrite);
            choices.Children.Add(keepBoth);

            var newName = new TextBox
            {
                Text = info.SuggestedNewName,
                Watermark = "New name",
                IsVisible = false,
                Margin = new Thickness(0, 6, 0, 0),
                MaxWidth = 380,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            // Show the rename field only while "Keep both" is selected.
            keepBoth.IsCheckedChanged += (_, _) => newName.IsVisible = keepBoth.IsChecked == true;

            var stack = new StackPanel { Spacing = 2 };
            stack.Children.Add(title);
            stack.Children.Add(meta);
            stack.Children.Add(choices);
            stack.Children.Add(newName);

            _rows.Add(new Row { Info = info, Skip = skip, Overwrite = overwrite, KeepBoth = keepBoth, NewName = newName });

            return new Border
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Child = stack
            };
        }

        private static string BuildMeta(ConflictInfo info)
        {
            string dst = Describe(info.HasDestStat, info.IsDirectory, info.DestSize, info.DestModified);
            string src = Describe(info.HasSourceStat, info.IsDirectory, info.SourceSize, info.SourceModified);

            string newerNote = "";
            if (info.HasSourceStat && info.HasDestStat && !info.IsDirectory)
            {
                if (info.SourceModified > info.DestModified) newerNote = "   (source is newer)";
                else if (info.DestModified > info.SourceModified) newerNote = "   (destination is newer)";
            }

            return $"At destination:  {dst}\nBeing {(info.IsDirectory ? "merged from" : "copied from")}:  {src}{newerNote}";
        }

        private static string Describe(bool hasStat, bool isDir, long size, DateTime modified)
        {
            if (!hasStat) return "unknown";
            string sizePart = isDir ? "folder" : ProgressFormat.Bytes(size);
            return $"{sizePart} · {modified:g}";
        }

        // ---- Bulk actions ----
        private void AllOverwrite_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.Overwrite.IsChecked = true;
        }

        private void AllSkip_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.Skip.IsChecked = true;
        }

        private void AllKeepBoth_Click(object? sender, RoutedEventArgs e)
        {
            foreach (var r in _rows) r.KeepBoth.IsChecked = true;
        }

        // ---- Footer ----
        private void Apply_Click(object? sender, RoutedEventArgs e)
        {
            Decisions.Clear();
            foreach (var r in _rows)
            {
                if (r.Skip.IsChecked == true)
                    Decisions[r.Info.Item.Source] = new ConflictDecision(ConflictResolution.Skip, null);
                else if (r.KeepBoth.IsChecked == true)
                {
                    string name = string.IsNullOrWhiteSpace(r.NewName.Text) ? r.Info.SuggestedNewName : r.NewName.Text!.Trim();
                    Decisions[r.Info.Item.Source] = new ConflictDecision(ConflictResolution.Rename, name);
                }
                else
                    Decisions[r.Info.Item.Source] = new ConflictDecision(ConflictResolution.Overwrite, null);
            }
            Close(true);
        }

        private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
