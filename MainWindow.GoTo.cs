using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// Go to Folder (Ctrl/Cmd+G): one list of the bookmarks, both panels' tabs, recent folders and
    /// disk catalogues' drives, searched as you type; a full path typed in goes straight there.
    /// Ctrl/Cmd+1…9 go to the first nine bookmarks.
    /// </summary>
    public partial class MainWindow
    {
        private void OpenGoToFolder(bool isLeft)
        {
            string side = isLeft ? "LP" : "RP";
            string current = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            string other = (isLeft ? RPpath.Text : LPpath.Text) ?? "";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            var places = new List<FolderPlace>();
            var bookmarks = Bookmarks.BookmarkStore.Load();
            for (int i = 0; i < bookmarks.Count; i++)
                places.Add(new FolderPlace(i < 9 ? $"Bookmark {i + 1}" : "Bookmark", bookmarks[i].Name, bookmarks[i].Path));
            places.Add(new FolderPlace("Other panel", Leaf(other), other));
            foreach (var t in _lpTabs.Concat(_rpTabs)) places.Add(new FolderPlace("Tab", Leaf(t.Path), t.Path));
            foreach (var r in _recent.Items) places.Add(new FolderPlace("Recent", Leaf(r), r));
            places.Add(new FolderPlace("Home", Leaf(home), home));
            try
            {
                foreach (var c in Catalogue.LoadAll()) places.Add(new FolderPlace("Catalogue", c.Name, c.Root));
            }
            catch { }

            var items = GoToFolder.Merge(places)
                .Where(p => !PathsEqual(p.Path, current))
                .Select(p => new PaletteItem(p.Group, $"{p.Name}   —   {p.Path}", "", () => GoTo(side, p.Path)))
                .ToList();

            IEnumerable<PaletteItem> Typed(string text)
            {
                if (GoToFolder.TypedPath(text, home) is { } path)
                    yield return new PaletteItem("Go to", path, "Enter", () => GoTo(side, path));
            }

            new CommandPaletteWindow(items, "Go to Folder",
                "Type part of a folder's name or path, or a full path — Enter goes there, Esc closes", Typed).ShowDialog(this);
        }

        private void GoToBookmark(bool isLeft, int number)
        {
            var bookmarks = Bookmarks.BookmarkStore.Load();
            if (number < 1 || number > bookmarks.Count)
            {
                if (isLeft) LeftStatusText.Text = $"There is no bookmark {number}."; else RightStatusText.Text = $"There is no bookmark {number}.";
                return;
            }
            GoTo(isLeft ? "LP" : "RP", bookmarks[number - 1].Path);
        }

        /// <summary>Goes there when the folder exists (cloud and archive paths are trusted), else says so.</summary>
        private void GoTo(string side, string path)
        {
            bool ok;
            try { ok = path.Contains("://") || ProviderRegistry.For(path).DirectoryExists(path); }
            catch { ok = false; }
            if (!ok)
            {
                _ = new MessageBox($"There is no folder {path}.", "Go to Folder").ShowDialog(this);
                return;
            }
            NavigatePanelTo(side, path);
        }

        private static string Leaf(string path)
        {
            string p = (path ?? "").TrimEnd('/', '\\');
            int cut = Math.Max(p.LastIndexOf('/'), p.LastIndexOf('\\'));
            string leaf = cut >= 0 ? p[(cut + 1)..] : p;
            return leaf.Length == 0 ? path ?? "" : leaf;
        }
    }
}
