using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>
    /// Archives inside archives: double-clicking a .zip (or .tar.gz, .7z…) shown inside another
    /// archive takes it out to a temporary copy and opens that in the panel. Going up from its root
    /// returns to the outer archive. Changes made to the inner archive (copying files into it,
    /// deleting, renaming) are offered back into the outer one, as edits to any file taken out of
    /// an archive are.
    /// </summary>
    public partial class MainWindow
    {
        private async void EnterNestedArchive(TaiDataGrid grid, TextBox pathBox, string current, string name)
        {
            bool isLeft = ReferenceEquals(grid, LPgrid);
            string entryUri = JoinChildPath(current, name);
            if (isLeft) LeftStatusText.Text = $"Opening {name}…"; else RightStatusText.Text = $"Opening {name}…";

            string? copy = await Task.Run(() => ExtractNestedArchive(entryUri, name));
            if (copy == null)
            {
                await new MessageBox($"\"{name}\" couldn't be taken out of the archive to open it.", "Archive").ShowDialog(this);
                UpdateStatusBar();
                return;
            }
            if (!PathsEqual(pathBox.Text ?? "", current)) return;   // the panel moved on meanwhile

            NestedArchives.Register(copy, entryUri);
            // Opened again: keep comparing with what was taken out, so changes not yet put back are still offered.
            if (_writeBack.Find(copy) == null) TrackArchiveCopy(entryUri, copy);
            NavigatePanelTo(isLeft ? "LP" : "RP", ArchivePath.RootUriFor(copy));
        }

        /// <summary>
        /// Copies the inner archive out to its own temp folder (named from the entry, so two inner
        /// archives with the same name never share a copy). Reuses an earlier copy of the same entry,
        /// so edits not yet put back aren't lost by opening it again.
        /// </summary>
        private string? ExtractNestedArchive(string entryUri, string name)
        {
            try
            {
                _toolTempDir ??= Path.Combine(Path.GetTempPath(), "dori-tool-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entryUri)))[..12].ToLowerInvariant();
                string dir = Path.Combine(_toolTempDir, "nested-" + key);
                string dest = Path.Combine(dir, Path.GetFileName(name.TrimEnd('/', '\\')));
                if (File.Exists(dest) && NestedArchives.OuterOf(dest) != null) return dest;

                Directory.CreateDirectory(dir);
                var provider = ProviderRegistry.For(entryUri);
                using (var input = provider.OpenRead(entryUri))
                using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                    input.CopyTo(output);
                return dest;
            }
            catch (Exception ex)
            {
                CrashLog.Write("Opening an archive inside an archive", ex);
                return null;
            }
        }
    }
}
