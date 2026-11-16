using System.IO;
using System.Threading.Tasks;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined
{
    /// <summary>Asking for the password of an encrypted archive before it is opened (kept for this session only).</summary>
    public partial class MainWindow
    {
        /// <summary>
        /// True when the archive can be opened now: it isn't encrypted, its password is known, or the
        /// right one was just typed. False when the person gave up.
        /// </summary>
        private async Task<bool> EnsureArchivePasswordAsync(string archiveFsPath, string? shownName = null)
        {
            if (!await Task.Run(() => ArchivePasswords.NeedsPassword(archiveFsPath))) return true;
            string name = shownName ?? Path.GetFileName(archiveFsPath);
            string prompt = $"{name} is password-protected. Type its password to open it.\n\n" +
                            "The password is kept only until DORI closes. Files can be copied out, but the archive can't be changed.";
            while (true)
            {
                var dialog = new TextPromptDialog("Password", prompt) { IsPassword = true };
                string? pw = await dialog.ShowDialog<string?>(this);
                if (pw == null) return false;
                if (await Task.Run(() => ArchivePasswords.Try(archiveFsPath, pw)))
                {
                    ArchivePasswords.Set(archiveFsPath, pw);
                    return true;
                }
                prompt = $"That password doesn't open {name}. Try again, or Cancel.";
            }
        }
    }
}
