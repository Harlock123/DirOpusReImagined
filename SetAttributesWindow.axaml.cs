using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined
{
    /// <summary>
    /// Sets the modified date, and the permissions (Linux, macOS) or read-only flag (Windows), of
    /// every selected item at once, optionally including everything inside selected folders.
    /// </summary>
    public partial class SetAttributesWindow : Window
    {
        private readonly List<(string Path, bool IsDirectory)> _selection = new();
        private readonly Action? _changed;

        public SetAttributesWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public SetAttributesWindow(string folder, List<(string Path, bool IsDirectory)> selection, Action changed) : this()
        {
            _selection = selection;
            _changed = changed;
            bool windows = OperatingSystem.IsWindows();

            string what = selection.Count == 1 ? $"\"{Path.GetFileName(selection[0].Path)}\"" : $"{selection.Count} items";
            this.FindControl<TextBlock>("HeaderText")!.Text = $"Change {what} in {folder}";
            this.FindControl<StackPanel>("UnixPanel")!.IsVisible = !windows;
            this.FindControl<StackPanel>("WindowsPanel")!.IsVisible = windows;
            this.FindControl<CheckBox>("ContentsBox")!.IsVisible = selection.Any(s => s.IsDirectory);

            var dateText = this.FindControl<TextBox>("DateText")!;
            try
            {
                var first = selection[0];
                dateText.Text = (first.IsDirectory ? Directory.GetLastWriteTime(first.Path) : File.GetLastWriteTime(first.Path))
                    .ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { dateText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); }

            // Typing in a box ticks its box: what's typed is clearly meant to be applied.
            dateText.TextChanged += (_, _) => this.FindControl<CheckBox>("DateBox")!.IsChecked = true;
            this.FindControl<Button>("NowButton")!.Click += (_, _) => dateText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var modeText = this.FindControl<TextBox>("ModeText")!;
            modeText.TextChanged += (_, _) =>
            {
                this.FindControl<CheckBox>("ModeBox")!.IsChecked = true;
                UpdateModePreview();
            };
            this.FindControl<Button>("CancelButton")!.Click += (_, _) => Close();
            this.FindControl<Button>("ApplyButton")!.Click += async (_, _) => await ApplyAsync();
            Opened += (_, _) => this.FindControl<CheckBox>("DateBox")!.IsChecked = false;   // the prefill above isn't a change
        }

        /// <summary>Shows what the typed permissions would do to the first selected item.</summary>
        private void UpdateModePreview()
        {
            if (OperatingSystem.IsWindows()) return;   // no Unix permissions there (this panel is hidden)
            var preview = this.FindControl<TextBlock>("ModePreview")!;
            string text = this.FindControl<TextBox>("ModeText")!.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) { preview.Text = ""; return; }
            if (!ModeChange.TryParse(text, out var change, out var error)) { preview.Text = "?"; ToolTip.SetTip(preview, error); return; }
            try
            {
                var (path, isDir) = _selection[0];
                var current = File.GetUnixFileMode(path);
                preview.Text = $"{ModeChange.Format(current)} → {ModeChange.Format(change.ApplyTo(current, isDir))}";
                ToolTip.SetTip(preview, $"For {Path.GetFileName(path)}");
            }
            catch { preview.Text = ""; }
        }

        private async Task ApplyAsync()
        {
            var status = this.FindControl<TextBlock>("StatusText")!;
            DateTime? when = null;
            if (this.FindControl<CheckBox>("DateBox")!.IsChecked == true)
            {
                if (!BatchAttributes.TryParseDate(this.FindControl<TextBox>("DateText")!.Text, out var d))
                {
                    status.Text = "The date isn't one DORI can read. Use the form 2026-11-15 14:30.";
                    return;
                }
                when = d;
            }

            ModeChange? mode = null;
            if (!OperatingSystem.IsWindows() && this.FindControl<CheckBox>("ModeBox")!.IsChecked == true)
            {
                if (!ModeChange.TryParse(this.FindControl<TextBox>("ModeText")!.Text, out var m, out var error))
                {
                    status.Text = error;
                    return;
                }
                mode = m;
            }

            bool? readOnly = OperatingSystem.IsWindows()
                ? this.FindControl<ComboBox>("ReadOnlyBox")!.SelectedIndex switch { 1 => true, 2 => false, _ => null }
                : null;

            if (when == null && mode == null && readOnly == null)
            {
                status.Text = "Tick what to change first.";
                return;
            }

            var changes = new AttributeChanges(when, mode, readOnly, this.FindControl<CheckBox>("ContentsBox")!.IsChecked == true);
            this.FindControl<Button>("ApplyButton")!.IsEnabled = false;
            status.Text = "Changing…";
            var (count, problems) = await Task.Run(() =>
            {
                var targets = BatchAttributes.Targets(_selection, changes.IncludeContents);
                return (targets.Count, BatchAttributes.Apply(targets, changes));
            });
            _changed?.Invoke();

            if (problems.Count == 0)
            {
                Close();
                return;
            }
            this.FindControl<Button>("ApplyButton")!.IsEnabled = true;
            status.Text = $"{count - problems.Count} of {count} items changed. These couldn't be:\n" +
                          string.Join("\n", problems.Take(8)) + (problems.Count > 8 ? $"\n… and {problems.Count - 8} more." : "");
        }
    }
}
