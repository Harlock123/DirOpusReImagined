using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace DirOpusReImagined
{
    /// <summary>
    /// Draws a <see cref="HexDocument"/> as offset, hex and text columns, only the rows on
    /// screen, so a file of any size scrolls at once. Arrow keys, Page Up/Down and Ctrl/Cmd+Home/End
    /// move the cursor; Tab switches between the hex and text columns. With <see cref="EditMode"/>
    /// on, typing hex digits (or characters in the text column) overwrites bytes.
    /// </summary>
    public sealed class HexView : Control
    {
        public const int PerRow = 16;
        private static readonly Typeface Mono = new("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,monospace");
        private const double FontSize = 13;

        private HexDocument? _doc;
        private long _top, _cursor;
        private bool _lowNibble;
        private double _charWidth, _lineHeight;
        private int _offsetDigits = 8;

        public HexView()
        {
            Focusable = true;
            ClipToBounds = true;
            var probe = new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, Brushes.Black);
            _charWidth = probe.WidthIncludingTrailingWhitespace;
            _lineHeight = Math.Ceiling(probe.Height * 1.2);
        }

        public HexDocument? Document
        {
            get => _doc;
            set
            {
                _doc = value;
                _top = _cursor = 0;
                _lowNibble = false;
                _offsetDigits = value == null ? 8 : HexDocument.OffsetDigits(value.Length);
                InvalidateVisual();
                CursorMoved?.Invoke();
                Scrolled?.Invoke();
            }
        }

        /// <summary>Typing changes bytes.</summary>
        public bool EditMode { get; set; }

        /// <summary>Typing goes to the text column (characters) rather than the hex column (digits).</summary>
        public bool InText { get; private set; }

        public long CursorOffset => _cursor;
        public long TopRow => _top;
        public long RowCount => _doc == null ? 0 : (_doc.Length + PerRow - 1) / PerRow;
        public int VisibleRows => Math.Max(1, (int)(Bounds.Height / _lineHeight));

        public event Action? CursorMoved;
        public event Action? Scrolled;
        /// <summary>A byte was changed by typing.</summary>
        public event Action? Edited;
        /// <summary>Typing was refused (read-only file, or edit mode off); the window explains.</summary>
        public event Action<string>? Refused;

        public void ScrollTo(long row)
        {
            long max = Math.Max(0, RowCount - VisibleRows);
            long r = Math.Clamp(row, 0, max);
            if (r == _top) return;
            _top = r;
            InvalidateVisual();
            Scrolled?.Invoke();
        }

        /// <summary>Puts the cursor on <paramref name="offset"/> and scrolls it into view.</summary>
        public void MoveTo(long offset, bool keepNibble = false)
        {
            if (_doc == null || _doc.Length == 0) return;
            _cursor = Math.Clamp(offset, 0, _doc.Length - 1);
            if (!keepNibble) _lowNibble = false;
            long row = _cursor / PerRow;
            if (row < _top) ScrollTo(row);
            else if (row >= _top + VisibleRows) ScrollTo(row - VisibleRows + 1);
            InvalidateVisual();
            CursorMoved?.Invoke();
        }

        /// <summary>Redraws after the document changed outside the view (undo, save).</summary>
        public void Refresh() => InvalidateVisual();

        private int HexStart => _offsetDigits + 2;
        private int HexColumn(int i) => HexStart + i * 3 + (i >= PerRow / 2 ? 1 : 0);
        private int TextStart => HexStart + PerRow * 3 + 2;

        public override void Render(DrawingContext ctx)
        {
            var bg = Brush("GridBackgroundBrush", Brushes.White);
            var fg = Brush("GridCellTextBrush", Brushes.Black);
            var muted = Brush("MutedTextBrush", Brushes.Gray);
            var accent = new SolidColorBrush(Color.FromRgb(0xE0, 0x40, 0x30));
            var cursorStrong = new SolidColorBrush(Color.FromArgb(0x90, 0x40, 0x80, 0xE0));
            var cursorWeak = new SolidColorBrush(Color.FromArgb(0x40, 0x40, 0x80, 0xE0));
            ctx.FillRectangle(bg, new Rect(Bounds.Size));
            if (_doc == null) return;

            Span<byte> row = stackalloc byte[PerRow];
            int rows = VisibleRows + 1;
            for (int r = 0; r < rows; r++)
            {
                long rowIndex = _top + r;
                long offset = rowIndex * PerRow;
                if (offset >= _doc.Length) break;
                int n = _doc.Read(offset, row);
                string line = HexDocument.FormatRow(offset, row[..n], PerRow, _offsetDigits);
                var text = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, FontSize, fg);
                text.SetForegroundBrush(muted, 0, _offsetDigits);
                for (int i = 0; i < n; i++)
                {
                    if (!_doc.IsEdited(offset + i)) continue;
                    text.SetForegroundBrush(accent, HexColumn(i), 2);
                    text.SetForegroundBrush(accent, TextStart + i, 1);
                }
                double y = r * _lineHeight;

                if (_cursor >= offset && _cursor < offset + n)
                {
                    int i = (int)(_cursor - offset);
                    double hx = HexColumn(i) * _charWidth;
                    double tx = (TextStart + i) * _charWidth;
                    if (!InText && EditMode)
                    {
                        ctx.FillRectangle(cursorWeak, new Rect(hx, y, _charWidth * 2, _lineHeight));
                        ctx.FillRectangle(cursorStrong, new Rect(hx + (_lowNibble ? _charWidth : 0), y, _charWidth, _lineHeight));
                    }
                    else ctx.FillRectangle(InText ? cursorWeak : cursorStrong, new Rect(hx, y, _charWidth * 2, _lineHeight));
                    ctx.FillRectangle(InText ? cursorStrong : cursorWeak, new Rect(tx, y, _charWidth, _lineHeight));
                }
                ctx.DrawText(text, new Point(0, y + (_lineHeight - text.Height) / 2));
            }
        }

        private IBrush Brush(string key, IBrush fallback)
            => this.TryFindResource(key, ActualThemeVariant, out var v) && v is IBrush b ? b : fallback;

        protected override Size MeasureOverride(Size availableSize)
            => new(Math.Min(availableSize.Width, (TextStart + PerRow + 1) * _charWidth),
                   double.IsInfinity(availableSize.Height) ? _lineHeight * 20 : availableSize.Height);

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            ScrollTo(_top);
            Scrolled?.Invoke();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            Focus();
            if (_doc == null) return;
            var p = e.GetPosition(this);
            long row = _top + (long)(p.Y / _lineHeight);
            int col = (int)(p.X / _charWidth);
            int index;
            if (col >= TextStart) { InText = true; index = Math.Min(PerRow - 1, col - TextStart); }
            else
            {
                InText = false;
                int c = col - HexStart;
                if (c >= PerRow / 2 * 3) c--;      // the extra space in the middle
                index = Math.Clamp(c / 3, 0, PerRow - 1);
            }
            MoveTo(row * PerRow + index);
            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            ScrollTo(_top - (long)Math.Round(e.Delta.Y * 3));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_doc == null || _doc.Length == 0) { base.OnKeyDown(e); return; }
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
            int page = Math.Max(1, VisibleRows - 1) * PerRow;
            switch (e.Key)
            {
                case Key.Left: MoveTo(_cursor - 1); break;
                case Key.Right: MoveTo(_cursor + 1); break;
                case Key.Up: MoveTo(_cursor - PerRow); break;
                case Key.Down: MoveTo(_cursor + PerRow); break;
                case Key.PageUp: ScrollTo(_top - VisibleRows + 1); MoveTo(_cursor - page); break;
                case Key.PageDown: ScrollTo(_top + VisibleRows - 1); MoveTo(_cursor + page); break;
                case Key.Home: MoveTo(ctrl ? 0 : _cursor / PerRow * PerRow); break;
                case Key.End: MoveTo(ctrl ? _doc.Length - 1 : Math.Min(_doc.Length - 1, _cursor / PerRow * PerRow + PerRow - 1)); break;
                case Key.Tab:
                    InText = !InText;
                    _lowNibble = false;
                    InvalidateVisual();
                    CursorMoved?.Invoke();
                    break;
                default:
                    base.OnKeyDown(e);
                    return;
            }
            e.Handled = true;
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);
            if (_doc == null || string.IsNullOrEmpty(e.Text) || _doc.Length == 0) return;
            if (!EditMode) { Refused?.Invoke("Turn on Edit to change bytes."); return; }
            if (_doc.ReadOnlyReason != null) { Refused?.Invoke("This file can't be changed: " + _doc.ReadOnlyReason + "."); return; }
            foreach (char c in e.Text)
            {
                if (InText)
                {
                    if (c < 0x20 || c > 0x7E) continue;
                    _doc.Set(_cursor, (byte)c);
                    Edited?.Invoke();
                    if (_cursor < _doc.Length - 1) MoveTo(_cursor + 1); else InvalidateVisual();
                }
                else
                {
                    int v = Uri.IsHexDigit(c) ? Convert.ToInt32(c.ToString(), 16) : -1;
                    if (v < 0) continue;
                    byte old = _doc.Get(_cursor);
                    byte b = _lowNibble ? (byte)((old & 0xF0) | v) : (byte)((v << 4) | (old & 0x0F));
                    _doc.Set(_cursor, b);
                    Edited?.Invoke();
                    if (!_lowNibble) { _lowNibble = true; InvalidateVisual(); CursorMoved?.Invoke(); }
                    else if (_cursor < _doc.Length - 1) MoveTo(_cursor + 1);
                    else { _lowNibble = false; InvalidateVisual(); }
                }
            }
            e.Handled = true;
        }
    }
}
