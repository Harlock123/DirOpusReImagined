using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace DirOpusReImagined
{
    /// <summary>The command line's earlier commands (newest last) and walking through them with ↑ and ↓.</summary>
    public sealed class CommandLineHistory
    {
        public const int MaxItems = 100;
        private readonly List<string> _items = new();
        private int _at;   // == Count when not walking

        public IReadOnlyList<string> Items => _items;

        public void Load(IEnumerable<string> items)
        {
            _items.Clear();
            _items.AddRange(items.Where(i => !string.IsNullOrWhiteSpace(i)).TakeLast(MaxItems));
            _at = _items.Count;
        }

        /// <summary>Remembers a command that was run; running one again moves it to the end.</summary>
        public void Add(string command)
        {
            command = command.Trim();
            if (command.Length == 0) return;
            _items.Remove(command);
            _items.Add(command);
            if (_items.Count > MaxItems) _items.RemoveAt(0);
            _at = _items.Count;
        }

        /// <summary>The command before the one shown (↑), or null at the oldest.</summary>
        public string? Older()
        {
            if (_at == 0) return null;
            _at--;
            return _items[_at];
        }

        /// <summary>The command after the one shown (↓); past the newest, an empty line.</summary>
        public string? Newer()
        {
            if (_at >= _items.Count) return null;
            _at++;
            return _at == _items.Count ? "" : _items[_at];
        }

        /// <summary>How a command line is run: the user's shell (or /bin/sh) with -c, or cmd.exe /c on Windows.</summary>
        public static (string File, string[] Args) ShellFor(string command)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", new[] { "/d", "/s", "/c", command });
            string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } s && System.IO.File.Exists(s) ? s : "/bin/sh";
            return (shell, new[] { "-c", command });
        }
    }
}
