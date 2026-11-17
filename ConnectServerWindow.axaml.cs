using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined
{
    /// <summary>
    /// Connect to Server (☁ → Connect to Server…): an SFTP, SMB, FTP or WebDAV server by its
    /// address and login, saved as an rclone remote. Closes with the panel path to open, or null.
    /// See <see cref="QuickConnect"/>.
    /// </summary>
    public partial class ConnectServerWindow : Window
    {
        private bool _filling;

        public ConnectServerWindow()
        {
            InitializeComponent();
            KindBox.SelectionChanged += (_, _) => { if (!_filling) KindChanged(resetPort: true); };
            TlsBox.IsCheckedChanged += (_, _) => { if (!_filling) KindChanged(resetPort: true); };
            HostBox.LostFocus += (_, _) => FillFromAddress();
            HostBox.TextChanged += (_, _) =>
            {
                string t = HostBox.Text ?? "";
                if (t.Contains("://") && t.IndexOf('/', t.IndexOf("://") + 3) > 0) FillFromAddress();
            };
            ConnectButton.Click += async (_, _) => await ConnectAsync();
            CancelButton.Click += (_, _) => Close(null);
            Opened += (_, _) => HostBox.Focus();
            KindChanged(resetPort: true);
        }

        private ServerKind Kind => (ServerKind)Math.Max(0, KindBox.SelectedIndex);

        private void KindChanged(bool resetPort)
        {
            var kind = Kind;
            KeyLabel.IsVisible = KeyBox.IsVisible = kind == ServerKind.Sftp;
            DomainLabel.IsVisible = DomainBox.IsVisible = kind == ServerKind.Smb;
            TlsBox.IsVisible = kind is ServerKind.Ftp or ServerKind.WebDav;
            FolderLabel.Text = kind == ServerKind.Smb ? "Share / folder" : "Folder";
            FolderBox.Watermark = kind switch
            {
                ServerKind.Sftp => "empty for your home folder; /var/www from the top",
                ServerKind.Smb => "share name, then any folder: photos/2024",
                ServerKind.WebDav => "the server's WebDAV path, for example remote.php/dav/files/me",
                _ => "optional",
            };
            PasswordBox.Watermark = kind == ServerKind.Sftp ? "(none: your SSH key or agent is used)" : "";
            if (resetPort) PortBox.Text = QuickConnect.DefaultPort(kind, TlsBox.IsChecked == true).ToString();
        }

        /// <summary>A whole address typed or pasted into Server fills in the other boxes.</summary>
        private void FillFromAddress()
        {
            string text = HostBox.Text ?? "";
            if (!text.Contains("://") && !text.StartsWith(@"\\") && !text.Contains('@')) return;
            var d = QuickConnect.ParseAddress(text, Kind);
            if (d == null) return;
            _filling = true;
            try
            {
                KindBox.SelectedIndex = (int)d.Kind;
                TlsBox.IsChecked = d.Tls;
                KindChanged(resetPort: false);
                HostBox.Text = d.Host;
                PortBox.Text = d.Port.ToString();
                if (d.User.Length > 0) UserBox.Text = d.User;
                if (d.Password.Length > 0) PasswordBox.Text = d.Password;
                if (d.Path.Length > 0) FolderBox.Text = d.Path;
            }
            finally { _filling = false; }
        }

        private async Task ConnectAsync()
        {
            FillFromAddress();
            string host = (HostBox.Text ?? "").Trim();
            if (host.Length == 0) { StatusText.Text = "Type the server's name or address."; return; }
            if (!int.TryParse(PortBox.Text, out int port) || port is < 1 or > 65535) { StatusText.Text = "The port should be a number from 1 to 65535."; return; }
            string key = (KeyBox.Text ?? "").Trim();
            if (key.StartsWith("~/")) key = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), key[2..]);
            var d = new ServerDetails(Kind, host, port, (UserBox.Text ?? "").Trim(), PasswordBox.Text ?? "",
                                      (FolderBox.Text ?? "").Trim().Replace('\\', '/'), key, (DomainBox.Text ?? "").Trim(), TlsBox.IsChecked == true);
            if (d.Kind == ServerKind.Smb && d.Path.Trim('/').Length == 0) { StatusText.Text = "Type the share's name under Share / folder."; return; }

            if (!RcloneService.IsInstalled())
            {
                StatusText.Text = "rclone isn't installed. Use the %RCLONECONFIG% button (Cloud setup) to install it, then try again.";
                return;
            }

            ConnectButton.IsEnabled = false;
            StatusText.Text = "Connecting…";
            string? created = null, alias = null;
            try
            {
                var taken = await RcloneRemoteManager.ListRemotesAsync();
                string? wanted = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text;
                string name = QuickConnect.RemoteName(d, taken, wanted);
                if (wanted != null && taken.Any(t => t.TrimEnd(':').Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    StatusText.Text = $"A remote called {name} already exists. Choose another name, or leave it empty.";
                    return;
                }
                var (type, parameters) = QuickConnect.ToRclone(d);
                await RcloneRemoteManager.CreateAsync(name, type, parameters, plainPasswords: true);
                created = name;

                string target;
                if (QuickConnect.NeedsAlias(d))
                {
                    alias = QuickConnect.RemoteName(d, taken.Append(name), name + "-" + d.Path.Trim('/').Replace('/', '-'));
                    await RcloneRemoteManager.CreateAsync(alias, "alias", QuickConnect.AliasParameters(name, d.Path));
                    target = QuickConnect.PanelPath(alias, "");
                }
                else target = QuickConnect.PanelPath(name, d.Path);

                // Look inside once, so a wrong password or address is said here rather than as an empty panel.
                await Task.Run(() => ProviderRegistry.For(target).EnumerateDirectories(target).Take(1).ToList());
                Close(target);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Couldn't connect: " + Shorten(ex.Message) + "\nCheck the address, the login and the folder, then try again.";
                // A server that can't be reached isn't kept, so trying again doesn't pile up remotes.
                foreach (var r in new[] { alias, created })
                    if (r != null) try { await RcloneRemoteManager.DeleteAsync(r); } catch { }
            }
            finally { ConnectButton.IsEnabled = true; }
        }

        private static string Shorten(string s) => s.Length > 300 ? s[..300] + "…" : s;
    }
}
