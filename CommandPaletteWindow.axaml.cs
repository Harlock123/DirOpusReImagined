using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined
{
    /// <summary>
    /// The command palette (Ctrl/Cmd+Shift+A): type part of a name, pick with the arrow keys, Enter
    /// runs it. Lists every panel command with its keys, every button in every bank, saved layouts
    /// and saved filters.
    /// </summary>
    public partial class CommandPaletteWindow : Window
    {
        private readonly IReadOnlyList<PaletteItem> _items = new List<PaletteItem>();
        private Func<string, IEnumerable<PaletteItem>>? _extra;

        /// <summary>
        /// A palette over other things than commands (Go to Folder uses it for folders), with
        /// <paramref name="extra"/> adding items made from what is typed (a path typed in full).
        /// </summary>
        public CommandPaletteWindow(IReadOnlyList<PaletteItem> items, string title, string watermark,
                                    Func<string, IEnumerable<PaletteItem>>? extra = null) : this(items)
        {
            Title = title;
            this.FindControl<TextBox>("QueryBox")!.Watermark = watermark;
            _extra = extra;
        }

        public CommandPaletteWindow()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public CommandPaletteWindow(IReadOnlyList<PaletteItem> items) : this()
        {
            _items = items;
            var query = this.FindControl<TextBox>("QueryBox")!;
            var list = this.FindControl<ListBox>("ResultsList")!;

            query.TextChanged += (_, _) => Show(query.Text ?? "");
            // Arrow keys move the choice while typing continues in the box.
            query.AddHandler(KeyDownEvent, (_, e) =>
            {
                int count = list.ItemCount;
                switch (e.Key)
                {
                    case Key.Down when count > 0: list.SelectedIndex = (list.SelectedIndex + 1) % count; list.ScrollIntoView(list.SelectedIndex); e.Handled = true; break;
                    case Key.Up when count > 0: list.SelectedIndex = (list.SelectedIndex - 1 + count) % count; list.ScrollIntoView(list.SelectedIndex); e.Handled = true; break;
                    case Key.Enter: RunSelected(); e.Handled = true; break;
                    case Key.Escape: Close(); e.Handled = true; break;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
            list.DoubleTapped += (_, _) => RunSelected();
            Opened += (_, _) => { Show(""); query.Focus(); };
        }

        private void Show(string text)
        {
            var list = this.FindControl<ListBox>("ResultsList")!;
            var ranked = CommandPalette.Rank(_items, text);
            if (_extra != null) ranked.InsertRange(0, _extra(text));
            list.ItemsSource = ranked;
            list.SelectedIndex = ranked.Count > 0 ? 0 : -1;
            this.FindControl<TextBlock>("HintText")!.Text = ranked.Count == 0
                ? "Nothing matches."
                : $"{ranked.Count} of {_items.Count} · ↑ ↓ choose · Enter run · Esc close";
        }

        private void RunSelected()
        {
            if (this.FindControl<ListBox>("ResultsList")!.SelectedItem is not PaletteItem item) return;
            Close();
            item.Run();
        }
    }
}
