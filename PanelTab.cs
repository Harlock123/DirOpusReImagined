using System.Collections.Generic;

namespace DirOpusReImagined;

/// <summary>
/// One folder tab within a panel. Holds the state that is otherwise "live" in MainWindow's per-panel
/// fields (path, back history, sort, filter, cursor). The active tab's state lives in those fields;
/// switching tabs snapshots the live fields into the outgoing tab and restores the incoming one.
/// </summary>
public sealed class PanelTab
{
    public string Path = "";
    public Stack<string> History = new();
    public SortSpec Sort = SortSpec.Default;
    public string FilterText = "";
    public int CursorIndex = -1;

    public PanelTab() { }

    public PanelTab(string path) { Path = path ?? ""; }

    /// <summary>Short label for the tab button — the final path segment, or the whole path if none.</summary>
    public string Title
    {
        get
        {
            var p = Path?.TrimEnd('/', '\\') ?? "";
            if (p.Length == 0) return "/";

            // Strip an archive:// entry marker so the tab shows the inner leaf, not the URI.
            int marker = p.IndexOf("!/", System.StringComparison.Ordinal);
            if (marker >= 0) p = p.Substring(marker + 2).TrimEnd('/', '\\');

            int slash = p.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? p.Substring(slash + 1) : p;
            return string.IsNullOrEmpty(name) ? p : name;
        }
    }
}
