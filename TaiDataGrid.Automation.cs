using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Automation.Peers;

namespace DirOpusReImagined
{
    /// <summary>
    /// What screen readers (Narrator, VoiceOver, Orca) hear from a panel. The grid draws its rows
    /// itself, so without this it would be one silent picture: it reports itself as a list named
    /// after its panel and folder, and says the row under the cursor ("photo.jpg, file, 2.3 MB,
    /// 3 of 40, selected") each time the cursor moves.
    /// </summary>
    public partial class TaiDataGrid
    {
        /// <summary>Set by the host: "Left panel, /home/me/Pictures".</summary>
        public string AccessibleName { get; set; } = "File list";

        private int _announcedCursor = -2;
        private int _announcedSelection = -1;

        protected override AutomationPeer OnCreateAutomationPeer() => new TaiDataGridPeer(this);

        /// <summary>The cursor row in words.</summary>
        internal string DescribeCursor()
        {
            if (_items.Count == 0) return "empty";
            if (CursorItem is not AFileEntry e) return $"{_items.Count} items, {SelectedItems.Count} selected";
            string kind = e.Name == ".." ? "parent folder" : e.Typ ? "folder" : "file";
            string size = !e.Typ && !string.IsNullOrWhiteSpace(e.FileSize) ? ", " + e.FileSize.Trim() : "";
            bool selected = SelectedItems.Contains(e);
            return $"{e.Name}, {kind}{size}, {_cursorIndex + 1} of {_items.Count}" + (selected ? ", selected" : "") +
                   (SelectedItems.Count > 1 ? $", {SelectedItems.Count} selected in all" : "");
        }

        /// <summary>Tells a listening screen reader when the cursor row or the selection changed.</summary>
        private void AnnounceCursorIfChanged()
        {
            if (_cursorIndex == _announcedCursor && SelectedItems.Count == _announcedSelection) return;
            _announcedCursor = _cursorIndex;
            _announcedSelection = SelectedItems.Count;
            // Only once something has asked for the peer, so this costs nothing without a screen reader.
            if (_peer is { } peer) peer.Changed();
        }

        internal TaiDataGridPeer? _peer;
    }

    internal sealed class TaiDataGridPeer : ControlAutomationPeer
    {
        private readonly TaiDataGrid _grid;
        private string _last = "";

        public TaiDataGridPeer(TaiDataGrid grid) : base(grid)
        {
            _grid = grid;
            grid._peer = this;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;
        protected override string GetClassNameCore() => "FileList";
        protected override string? GetNameCore() => _grid.AccessibleName + ": " + _grid.DescribeCursor();
        protected override string? GetHelpTextCore() => "Arrow keys move, Space selects, Enter opens, F3 views, Ctrl+Shift+A opens the command palette.";
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;

        public void Changed()
        {
            string now = GetName() ?? "";
            if (now == _last) return;
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, _last, now);
            _last = now;
        }
    }
}
