using System;
using System.Collections.Generic;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>The command palette (Ctrl/Cmd+Shift+A, or Commands… beside the button bank tabs).</summary>
    public partial class MainWindow
    {
        private void OpenCommandPalette()
        {
            new CommandPaletteWindow(PaletteItems()).ShowDialog(this);
        }

        /// <summary>Everything the palette offers, in the order shown before anything is typed.</summary>
        private List<PaletteItem> PaletteItems()
        {
            bool mac = OperatingSystem.IsMacOS();
            var panel = _activeGrid ?? LPgrid;
            bool left = ReferenceEquals(panel, LPgrid);
            var items = new List<PaletteItem>();

            foreach (var c in KeyMap.Commands.Where(c => c.Command != ShortcutCommand.CommandPalette))
            {
                var command = c.Command;
                items.Add(new PaletteItem("Command", c.Title, KeyMap.Current.DisplayFor(command, mac), () => panel.RunShortcut(command)));
            }

            items.Add(new PaletteItem("Window", "Find files (search this folder)", "", () => OpenSearch(left ? "LP" : "RP")));
            items.Add(new PaletteItem("Window", "Bookmarks", "", () => BookmarksButton_Click(null, new Avalonia.Interactivity.RoutedEventArgs())));
            items.Add(new PaletteItem("Window", "Edit buttons and banks", "", OpenButtonEditor));
            items.Add(new PaletteItem("Window", "General help", "", () => GeneralHelpButton_Click(null, new Avalonia.Interactivity.RoutedEventArgs())));

            // Every button in every bank, not just the one showing.
            foreach (var bank in _banks)
                foreach (var s in bank.Buttons.Where(b => !string.IsNullOrWhiteSpace(b.Content) && !string.IsNullOrWhiteSpace(b.Action)))
                {
                    var entry = ToEntry(s);
                    string where = _banks.Count > 1 ? $"  ({bank.Name})" : "";
                    items.Add(new PaletteItem("Button", s.Content.Trim() + where, "", () => RunButtonEntry(entry)));
                }

            foreach (var job in _syncJobs)
            {
                var j = job;
                items.Add(new PaletteItem("Sync job", j.Name, "", () => _ = RunSyncJobAsync(j, byItself: false)));
            }

            foreach (var layout in ReadLayouts())
            {
                var chosen = layout;
                items.Add(new PaletteItem("Layout", layout.Name, "", () => _ = ApplyLayoutAsync(chosen)));
            }

            foreach (var filter in ReadSavedFilters())
            {
                var pattern = filter.Pattern;
                items.Add(new PaletteItem("Filter", filter.MenuLabel, "", () => SetPanelFilter(left, pattern)));
            }
            return items;
        }

        /// <summary>A button's settings as the runnable entry its click would use.</summary>
        private static ButtonEntry ToEntry(ButtonSettings s)
        {
            var entry = new ButtonEntry(s.Name, s.Action, s.Args) { ToolTip = s.ToolTip };
            if (!string.IsNullOrEmpty(s.ShellExecute) && bool.TryParse(s.ShellExecute, out bool se)) entry.ShellExecute = se;
            if (!string.IsNullOrEmpty(s.ShowWindow) && bool.TryParse(s.ShowWindow, out bool sw)) entry.ShowWindow = sw;
            return entry;
        }
    }
}
