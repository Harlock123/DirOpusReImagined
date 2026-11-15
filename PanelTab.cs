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
    public Stack<string> Forward = new();
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
            var raw = Path ?? "";

            // An archive:// path: show the inner leaf, or the archive's own name at its root. The
            // "!/" marker is found before trimming, since at the root the trailing '/' is part of it.
            string p;
            int marker = raw.IndexOf("!/", System.StringComparison.Ordinal);
            if (marker >= 0)
            {
                p = raw.Substring(marker + 2).TrimEnd('/', '\\');
                if (p.Length == 0) p = raw.Substring(0, marker);
            }
            else
            {
                p = raw.TrimEnd('/', '\\');
            }
            if (p.Length == 0) return "/";

            int slash = p.LastIndexOfAny(new[] { '/', '\\' });
            string name = slash >= 0 ? p.Substring(slash + 1) : p;
            return string.IsNullOrEmpty(name) ? p : name;
        }
    }
}
