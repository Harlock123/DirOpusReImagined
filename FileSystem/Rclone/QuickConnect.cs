using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DirOpusReImagined.FileSystem.Rclone;

/// <summary>The kinds of server Connect to Server knows, each an rclone backend.</summary>
public enum ServerKind { Sftp, Smb, Ftp, WebDav }

/// <summary>
/// A server typed into Connect to Server. <see cref="Path"/> is the folder to open: for SMB its
/// first part is the share. <see cref="KeyFile"/> is an SSH private key (SFTP only); with neither
/// a password nor a key, SFTP uses the SSH agent.
/// </summary>
public sealed record ServerDetails(ServerKind Kind, string Host, int Port, string User, string Password,
                                   string Path, string KeyFile = "", string Domain = "", bool Tls = false);

/// <summary>
/// Turns a server's address and login into an rclone remote, so SFTP, SMB (Windows shares), FTP
/// and WebDAV servers open in a panel without the user learning rclone's configuration. The
/// remote is saved like any other, so it is listed under ☁ next time. Passwords are given to
/// rclone in the clear and rclone obscures them before writing its config file.
/// </summary>
public static class QuickConnect
{
    public static int DefaultPort(ServerKind kind, bool tls = false) => kind switch
    {
        ServerKind.Sftp => 22,
        ServerKind.Smb => 445,
        ServerKind.Ftp => 21,
        ServerKind.WebDav => tls ? 443 : 80,
        _ => 0,
    };

    public static string Label(ServerKind kind) => kind switch
    {
        ServerKind.Sftp => "SFTP (SSH)",
        ServerKind.Smb => "SMB (Windows share)",
        ServerKind.Ftp => "FTP",
        ServerKind.WebDav => "WebDAV",
        _ => kind.ToString(),
    };

    /// <summary>
    /// Reads an address such as <c>sftp://me@host:2222/home/me</c>, <c>smb://nas/photos</c>,
    /// <c>\\nas\photos</c>, <c>ftps://host</c> or <c>https://host/dav</c>. A bare host name keeps
    /// <paramref name="kind"/>. Null when nothing usable was typed.
    /// </summary>
    public static ServerDetails? ParseAddress(string text, ServerKind kind)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return null;

        // \\server\share\folder — a Windows share.
        if (text.StartsWith(@"\\"))
        {
            var parts = text.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return null;
            return new ServerDetails(ServerKind.Smb, parts[0], DefaultPort(ServerKind.Smb), "", "", string.Join("/", parts.Skip(1)));
        }

        bool tls = false;
        int scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme > 0)
        {
            string s = text[..scheme].ToLowerInvariant();
            (kind, tls) = s switch
            {
                "sftp" or "ssh" or "scp" => (ServerKind.Sftp, false),
                "smb" or "cifs" => (ServerKind.Smb, false),
                "ftp" => (ServerKind.Ftp, false),
                "ftps" => (ServerKind.Ftp, true),
                "http" or "dav" or "webdav" => (ServerKind.WebDav, false),
                "https" or "davs" or "webdavs" => (ServerKind.WebDav, true),
                _ => (kind, false),
            };
            text = text[(scheme + 3)..];
        }

        string path = "";
        int slash = text.IndexOf('/');
        if (slash >= 0) { path = text[(slash + 1)..]; text = text[..slash]; }

        string user = "", password = "";
        int at = text.LastIndexOf('@');
        if (at >= 0)
        {
            string login = Uri.UnescapeDataString(text[..at]);
            text = text[(at + 1)..];
            int colon = login.IndexOf(':');
            if (colon >= 0) { user = login[..colon]; password = login[(colon + 1)..]; }
            else user = login;
        }

        string host = text;
        int port = DefaultPort(kind, tls);
        // host:port, but not an IPv6 address in brackets.
        int portColon = host.LastIndexOf(':');
        if (portColon > 0 && host.IndexOf(']') < portColon && int.TryParse(host[(portColon + 1)..], out int p) && p is > 0 and < 65536)
        {
            port = p;
            host = host[..portColon];
        }
        if (host.Length == 0) return null;
        string folder = Uri.UnescapeDataString(path).Trim('/');
        // In an sftp:// address the path starts at the server's top folder, as in other file managers.
        if (kind == ServerKind.Sftp && folder.Length > 0) folder = "/" + folder;
        return new ServerDetails(kind, host, port, user, password, folder, Tls: tls);
    }

    /// <summary>The rclone backend and its settings for <paramref name="d"/>.</summary>
    public static (string Type, Dictionary<string, string> Parameters) ToRclone(ServerDetails d)
    {
        var p = new Dictionary<string, string>();
        void Put(string key, string value) { if (!string.IsNullOrEmpty(value)) p[key] = value; }

        switch (d.Kind)
        {
            case ServerKind.Sftp:
                Put("host", d.Host);
                Put("user", d.User);
                if (d.Port != DefaultPort(ServerKind.Sftp)) Put("port", d.Port.ToString());
                Put("pass", d.Password);
                Put("key_file", d.KeyFile);
                if (string.IsNullOrEmpty(d.Password) && string.IsNullOrEmpty(d.KeyFile)) p["key_use_agent"] = "true";
                return ("sftp", p);
            case ServerKind.Smb:
                Put("host", d.Host);
                Put("user", d.User);
                if (d.Port != DefaultPort(ServerKind.Smb)) Put("port", d.Port.ToString());
                Put("pass", d.Password);
                Put("domain", d.Domain);
                return ("smb", p);
            case ServerKind.Ftp:
                Put("host", d.Host);
                Put("user", d.User);
                if (d.Port != DefaultPort(ServerKind.Ftp)) Put("port", d.Port.ToString());
                Put("pass", d.Password);
                if (d.Tls) p["explicit_tls"] = "true";
                return ("ftp", p);
            case ServerKind.WebDav:
                string scheme = d.Tls ? "https" : "http";
                string port = d.Port == DefaultPort(ServerKind.WebDav, d.Tls) ? "" : ":" + d.Port;
                // The URL is the server's WebDAV root; the folder typed after it is opened inside.
                p["url"] = $"{scheme}://{d.Host}{port}/";
                p["vendor"] = "other";
                Put("user", d.User);
                Put("pass", d.Password);
                return ("webdav", p);
        }
        throw new ArgumentOutOfRangeException(nameof(d));
    }

    /// <summary>
    /// A name for the remote: "sftp-myserver", made of letters, digits, '-' and '_', and not one
    /// of <paramref name="taken"/> ("sftp-myserver-2" then).
    /// </summary>
    public static string RemoteName(ServerDetails d, IEnumerable<string> taken, string? wanted = null)
    {
        string raw = string.IsNullOrWhiteSpace(wanted)
            ? $"{d.Kind.ToString().ToLowerInvariant()}-{(d.User.Length > 0 ? d.User + "-" : "")}{d.Host}"
            : wanted.Trim();
        var sb = new StringBuilder();
        foreach (char c in raw)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        string name = sb.ToString().Trim('-');
        while (name.Contains("--")) name = name.Replace("--", "-");
        if (name.Length == 0) name = "server";

        var set = new HashSet<string>(taken.Select(t => t.TrimEnd(':')), StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(name) || !string.IsNullOrWhiteSpace(wanted)) return name;
        for (int i = 2; ; i++)
            if (!set.Contains($"{name}-{i}")) return $"{name}-{i}";
    }

    /// <summary>
    /// An SFTP folder given from the server's top ("/var/www") can't be reached through a plain
    /// sftp remote, whose paths start in the login's home folder, so it gets an alias remote
    /// pointing at it.
    /// </summary>
    public static bool NeedsAlias(ServerDetails d)
        => d.Kind == ServerKind.Sftp && d.Path.StartsWith('/') && d.Path.Trim('/').Length > 0;

    /// <summary>The settings of an alias remote that opens <paramref name="path"/> on <paramref name="baseRemote"/>.</summary>
    public static Dictionary<string, string> AliasParameters(string baseRemote, string path)
        => new() { ["remote"] = $"{baseRemote}:/{path.Trim('/')}" };

    /// <summary>The panel path for the folder on the new remote ("cloud://name/path").</summary>
    public static string PanelPath(string remote, string path)
    {
        string folder = (path ?? "").Replace('\\', '/').Trim('/');
        return $"{CloudPath.Scheme}{remote}/{folder}";
    }
}
