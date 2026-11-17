using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The hex editor (Shift+F3, or Hex Editor in the viewer): the whole file in hex and text,
    /// going to an offset, finding bytes or text, and changing bytes in place. See <see cref="HexDocument"/>.
    /// </summary>
    public partial class HexEditorWindow : Window
    {
        private HexDocument? _doc;
        private string _name = "";
        private bool _closingAfterAsk, _scrolling;

        /// <summary>Raised after a save with the file's path, so panels can refresh.</summary>
        public event Action<string>? Saved;

        public HexEditorWindow()
        {
            InitializeComponent();
            View.CursorMoved += UpdateStatus;
            View.Scrolled += SyncScrollBar;
            View.Edited += () => { UpdateButtons(); UpdateStatus(); };
            View.Refused += msg => ValueText.Text = msg;
            VScroll.ValueChanged += (_, e) =>
            {
                if (_scrolling) return;
                View.ScrollTo((long)Math.Round(e.NewValue));
            };
            EditToggle.IsCheckedChanged += (_, _) =>
            {
                View.EditMode = EditToggle.IsChecked == true;
                View.Refresh();
                UpdateStatus();
                View.Focus();
            };
            UndoButton.Click += (_, _) => Undo();
            SaveButton.Click += async (_, _) => await SaveAsync();
            GoButton.Click += (_, _) => GoTo();
            GoToBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { GoTo(); e.Handled = true; } };
            FindNextButton.Click += async (_, _) => await FindAsync(true);
            FindPrevButton.Click += async (_, _) => await FindAsync(false);
            FindBox.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await FindAsync(true); } };
            CloseButton.Click += (_, _) => Close();
            AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
            Closing += OnClosingWithChanges;
            Closed += (_, _) => _doc?.Dispose();
        }

        public HexEditorWindow(string path, string name) : this()
        {
            _name = name;
            Title = "Hex Editor — " + name;
            FileText.Text = name;
            Opened += async (_, _) => await OpenAsync(path);
        }

        private async Task OpenAsync(string path)
        {
            PositionText.Text = "reading…";
            try
            {
                _doc = await Task.Run(() => HexDocument.Open(path));
            }
            catch (Exception ex)
            {
                PositionText.Text = "Couldn't open it: " + ex.Message;
                return;
            }
            View.Document = _doc;
            FileText.Text = $"{_name}   ·   {ProgressFormat.Bytes(_doc.Length)} ({_doc.Length:N0} bytes)";
            if (_doc.ReadOnlyReason != null)
            {
                EditToggle.IsEnabled = false;
                ToolTip.SetTip(EditToggle, "Can't be changed: " + _doc.ReadOnlyReason);
            }
            SyncScrollBar();
            UpdateStatus();
            View.Focus();
        }

        private void OnWindowKey(object? sender, KeyEventArgs e)
        {
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            if (ctrl && e.Key == Key.S) { _ = SaveAsync(); e.Handled = true; }
            else if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; }
            else if (ctrl && e.Key == Key.G) { GoToBox.Focus(); GoToBox.SelectAll(); e.Handled = true; }
            else if (ctrl && e.Key == Key.F) { FindBox.Focus(); FindBox.SelectAll(); e.Handled = true; }
            else if (e.Key == Key.F3) { _ = FindAsync(!e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
            else if (e.Key == Key.Escape && !View.IsFocused) { View.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }

        private void SyncScrollBar()
        {
            _scrolling = true;
            try
            {
                long max = Math.Max(0, View.RowCount - View.VisibleRows);
                VScroll.Maximum = max;
                VScroll.ViewportSize = View.VisibleRows;
                VScroll.LargeChange = Math.Max(1, View.VisibleRows - 1);
                VScroll.SmallChange = 1;
                VScroll.Value = View.TopRow;
            }
            finally { _scrolling = false; }
        }

        private void UpdateStatus()
        {
            if (_doc == null) return;
            long c = View.CursorOffset;
            PositionText.Text = $"Offset 0x{c:X} ({c:N0}) of {_doc.Length:N0}" +
                                (_doc.ChangeCount > 0 ? $"   ·   {_doc.ChangeCount:N0} byte(s) changed, not saved" : "") +
                                (View.EditMode ? View.InText ? "   ·   typing characters (Tab: hex)" : "   ·   typing hex (Tab: characters)" : "");
            ValueText.Text = _doc.DescribeAt(c);
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            SaveButton.IsEnabled = _doc?.IsChanged == true;
            UndoButton.IsEnabled = _doc?.CanUndo == true;
            Title = "Hex Editor — " + _name + (_doc?.IsChanged == true ? " •" : "");
        }

        private void Undo()
        {
            if (_doc == null) return;
            long at = _doc.Undo();
            if (at >= 0) View.MoveTo(at);
            View.Refresh();
            UpdateStatus();
        }

        private void GoTo()
        {
            if (_doc == null) return;
            var offset = HexDocument.ParseOffset(GoToBox.Text ?? "", _doc.Length);
            if (offset == null || offset >= _doc.Length) { ValueText.Text = $"That isn't an offset in this file (0 to 0x{Math.Max(0, _doc.Length - 1):X})."; return; }
            View.MoveTo(offset.Value);
            View.Focus();
        }

        private async Task FindAsync(bool forward)
        {
            if (_doc == null) return;
            var pattern = HexDocument.ParsePattern(FindBox.Text ?? "", HexFindBox.IsChecked == true);
            if (pattern == null)
            {
                ValueText.Text = HexFindBox.IsChecked == true ? "Type hex bytes, for example DE AD BE EF." : "Type what to find.";
                return;
            }
            long from = forward ? View.CursorOffset + 1 : View.CursorOffset - 1;
            PositionText.Text = "finding…";
            var doc = _doc;
            long hit = await Task.Run(() => doc.Find(pattern, from, forward));
            if (hit < 0)
            {
                UpdateStatus();
                ValueText.Text = forward ? "Not found after the cursor." : "Not found before the cursor.";
                return;
            }
            View.MoveTo(hit);
            View.Focus();
        }

        private async Task<bool> SaveAsync()
        {
            if (_doc is not { IsChanged: true } doc) return true;
            PositionText.Text = "saving…";
            try
            {
                await Task.Run(doc.Save);
            }
            catch (Exception ex)
            {
                UpdateStatus();
                await new MessageBox($"{_name} couldn't be saved: {ex.Message}", "Hex Editor").ShowDialog(this);
                return false;
            }
            View.Refresh();
            UpdateStatus();
            ValueText.Text = $"Saved {DateTime.Now:t}.";
            Saved?.Invoke(doc.Path);
            return true;
        }

        private async void OnClosingWithChanges(object? sender, WindowClosingEventArgs e)
        {
            if (_doc?.IsChanged != true || _closingAfterAsk) return;
            e.Cancel = true;
            bool save = await new MessageBox($"Save the changed bytes in {_name}?", true, "Save", "Don't Save", "Hex Editor").ShowDialog<bool>(this);
            if (save && !await SaveAsync()) return;
            _closingAfterAsk = true;
            Close();
        }
    }
}
