using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Input;

namespace DirOpusReImagined
{
    /// <summary>Everything a panel shortcut can do. The names are what &lt;Shortcuts&gt; in Configuration.xml uses.</summary>
    public enum ShortcutCommand
    {
        Rename, View, Copy, Move, NewFolder, Delete, PreviewToggle,
        Refresh, Undo, FlatView, CompareFiles, FindDuplicates, SetAttributes, Checksums, GoToLinkTarget, MakeLink,
        RecentFolders, SavedFilters, NextButtonBank, PreviousButtonBank,
        NewTab, CloseTab, NextTab, PreviousTab,
        GoUp, Forward, SwitchPanel,
        SelectAll, SelectByPattern, DeselectByPattern, InvertSelection,
        CommandPalette,
    }

    /// <summary>
    /// A key and its modifiers. "Ctrl" means Ctrl on Windows and Linux and Cmd on macOS, as every
    /// shortcut in DORI always has.
    /// </summary>
    public readonly record struct Shortcut(Key Key, bool Ctrl, bool Shift, bool Alt)
    {
        private static readonly Dictionary<string, Key> Aliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["PgUp"] = Key.PageUp, ["PgDn"] = Key.PageDown, ["PageUp"] = Key.PageUp, ["PageDown"] = Key.PageDown,
            ["Del"] = Key.Delete, ["Esc"] = Key.Escape, ["Backspace"] = Key.Back, ["Ins"] = Key.Insert,
            ["Plus"] = Key.OemPlus, ["Minus"] = Key.OemMinus,
            ["NumPlus"] = Key.Add, ["NumMinus"] = Key.Subtract, ["NumStar"] = Key.Multiply,
        };

        /// <summary>Reads "Ctrl+Shift+F", "Alt+PageDown", "F5" or "Delete" (any case; "Cmd" and "Control" mean Ctrl, "Option" means Alt).</summary>
        public static bool TryParse(string? text, out Shortcut shortcut)
        {
            shortcut = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text.Split('+').Select(p => p.Trim()).ToList();
            if (parts.Count == 0 || parts.Any(p => p.Length == 0)) return false;

            bool ctrl = false, shift = false, alt = false;
            foreach (var mod in parts.Take(parts.Count - 1))
            {
                switch (mod.ToLowerInvariant())
                {
                    case "ctrl": case "control": case "cmd": case "command": case "meta": ctrl = true; break;
                    case "shift": shift = true; break;
                    case "alt": case "option": case "opt": alt = true; break;
                    default: return false;
                }
            }

            if (!TryParseKey(parts[^1], out Key key)) return false;
            shortcut = new Shortcut(key, ctrl, shift, alt);
            return true;
        }

        private static bool TryParseKey(string name, out Key key)
        {
            if (Aliases.TryGetValue(name, out key)) return true;
            if (name.Length == 1 && char.IsDigit(name[0])) return Enum.TryParse("D" + name, out key);
            // Enum.TryParse also takes numbers ("42"), which aren't key names.
            return !name.All(char.IsDigit) && Enum.TryParse(name, ignoreCase: true, out key) && Enum.IsDefined(key);
        }

        /// <summary>The form Configuration.xml uses, which <see cref="TryParse"/> reads back.</summary>
        public override string ToString()
            => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + Key;

        /// <summary>As people know the key: "Cmd" on macOS, "Num +" for the keypad, "PgDn".</summary>
        public string Display(bool mac)
        {
            string key = Key switch
            {
                Key.PageUp => "PgUp", Key.PageDown => "PgDn", Key.Back => "Backspace",
                Key.Add => "Num +", Key.Subtract => "Num -", Key.Multiply => "Num *",
                Key.OemPlus => Shift ? "+" : "=", Key.OemMinus => "-",
                Key.D8 when Shift => "*",
                >= Key.D0 and <= Key.D9 => ((int)(Key - Key.D0)).ToString(CultureInfo.InvariantCulture),
                _ => Key.ToString(),
            };
            // A typed "+" or "*" already implies Shift.
            bool showShift = Shift && !(Key == Key.OemPlus || (Key == Key.D8 && Shift));
            return (Ctrl ? (mac ? "Cmd+" : "Ctrl+") : "") + (showShift ? "Shift+" : "") + (Alt ? (mac ? "Option+" : "Alt+") : "") + key;
        }

        public static Shortcut From(Key key, KeyModifiers mods)
            => new(key,
                   mods.HasFlag(KeyModifiers.Control) || mods.HasFlag(KeyModifiers.Meta),
                   mods.HasFlag(KeyModifiers.Shift),
                   mods.HasFlag(KeyModifiers.Alt));
    }

    /// <summary>
    /// Which keys run which commands in a panel. Starts from the defaults below; a &lt;Shortcuts&gt;
    /// section in Configuration.xml changes them:
    /// <code>
    /// &lt;Shortcuts&gt;
    ///   &lt;Shortcut command="Copy" keys="F5, Ctrl+Shift+C" /&gt;   (replaces Copy's keys)
    ///   &lt;Shortcut command="Delete" keys="" /&gt;               (Delete has no key)
    /// &lt;/Shortcuts&gt;
    /// </code>
    /// A key given to a command is taken away from whichever command had it by default.
    /// Moving the cursor (arrows, Home/End, Page Up/Down, Space, Insert, Enter) isn't configurable.
    /// </summary>
    public sealed class KeyMap
    {
        public sealed record CommandInfo(ShortcutCommand Command, string Title, string Defaults);

        /// <summary>Every command, its name for people, and its default keys.</summary>
        public static readonly IReadOnlyList<CommandInfo> Commands = new CommandInfo[]
        {
            new(ShortcutCommand.Rename, "Rename", "F2"),
            new(ShortcutCommand.View, "View file", "F3"),
            new(ShortcutCommand.Copy, "Copy to the other panel", "F5"),
            new(ShortcutCommand.Move, "Move to the other panel", "F6"),
            new(ShortcutCommand.NewFolder, "New folder", "F7"),
            new(ShortcutCommand.Delete, "Delete", "F8, Delete, Shift+Delete"),
            new(ShortcutCommand.PreviewToggle, "Preview window on / off", "F9"),
            new(ShortcutCommand.Refresh, "Refresh", "Ctrl+R"),
            new(ShortcutCommand.Undo, "Undo", "Ctrl+Z"),
            new(ShortcutCommand.FlatView, "Flat view on / off", "Ctrl+Shift+F"),
            new(ShortcutCommand.CompareFiles, "Compare files", ""),
            new(ShortcutCommand.FindDuplicates, "Find duplicate files", ""),
            new(ShortcutCommand.SetAttributes, "Set dates and permissions", ""),
            new(ShortcutCommand.Checksums, "Checksums", ""),
            new(ShortcutCommand.GoToLinkTarget, "Go to link target", ""),
            new(ShortcutCommand.MakeLink, "Make link in the other panel", ""),
            new(ShortcutCommand.RecentFolders, "Recent folders", "Alt+Down"),
            new(ShortcutCommand.SavedFilters, "Saved filters menu", ""),
            new(ShortcutCommand.NextButtonBank, "Next button bank", "Alt+PageDown"),
            new(ShortcutCommand.PreviousButtonBank, "Previous button bank", "Alt+PageUp"),
            new(ShortcutCommand.NewTab, "New tab", "Ctrl+T"),
            new(ShortcutCommand.CloseTab, "Close tab", "Ctrl+W"),
            new(ShortcutCommand.NextTab, "Next tab", "Ctrl+Tab, Ctrl+PageDown"),
            new(ShortcutCommand.PreviousTab, "Previous tab", "Ctrl+Shift+Tab, Ctrl+PageUp"),
            new(ShortcutCommand.GoUp, "Back / up a folder", "Backspace, Alt+Left"),
            new(ShortcutCommand.Forward, "Forward", "Alt+Right"),
            new(ShortcutCommand.SwitchPanel, "Switch panel", "Tab, Shift+Tab"),
            new(ShortcutCommand.SelectAll, "Select all files", "Ctrl+A"),
            new(ShortcutCommand.SelectByPattern, "Select by pattern", "NumPlus, Shift+Plus"),
            new(ShortcutCommand.DeselectByPattern, "Deselect by pattern", "NumMinus, Minus"),
            new(ShortcutCommand.InvertSelection, "Invert selection", "NumStar, Shift+8"),
            new(ShortcutCommand.CommandPalette, "Command palette (find any command or button)", "Ctrl+Shift+A"),
        };

        public const string ElementName = "Shortcuts";

        private readonly Dictionary<Shortcut, ShortcutCommand> _byKey = new();
        private readonly Dictionary<ShortcutCommand, List<Shortcut>> _byCommand = new();

        /// <summary>The map the panels use. Set from the configuration at start-up.</summary>
        public static KeyMap Current { get; set; } = FromXml(null, out _);

        public bool TryGet(Key key, KeyModifiers mods, out ShortcutCommand command)
            => _byKey.TryGetValue(Shortcut.From(key, mods), out command);

        /// <summary>The keys that run <paramref name="command"/>, in the order they were given.</summary>
        public IReadOnlyList<Shortcut> KeysFor(ShortcutCommand command)
            => _byCommand.TryGetValue(command, out var list) ? list : Array.Empty<Shortcut>();

        /// <summary>"F8, Delete", or "" when the command has no key.</summary>
        public string DisplayFor(ShortcutCommand command, bool mac)
            => string.Join(", ", KeysFor(command).Select(s => s.Display(mac)));

        /// <summary>
        /// The defaults with the configuration's &lt;Shortcuts&gt; applied. Anything that can't be
        /// understood (an unknown command or key) is skipped and described in <paramref name="problems"/>.
        /// </summary>
        public static KeyMap FromXml(XElement? root, out List<string> problems)
        {
            problems = new List<string>();
            var keys = Commands.ToDictionary(c => c.Command, c => Parse(c.Defaults, null));
            var overridden = new List<ShortcutCommand>();

            foreach (var el in root?.Element(ElementName)?.Elements("Shortcut") ?? Enumerable.Empty<XElement>())
            {
                string name = ((string?)el.Attribute("command") ?? "").Trim();
                if (!Enum.TryParse(name, ignoreCase: true, out ShortcutCommand command) || !Enum.IsDefined(command))
                {
                    problems.Add($"\"{name}\" isn't a command DORI knows.");
                    continue;
                }
                keys[command] = Parse((string?)el.Attribute("keys") ?? "", problems);
                overridden.Remove(command);
                overridden.Add(command);
            }

            var map = new KeyMap();
            // Defaults first, then each changed command in the order given, so a key claimed by a
            // change is taken from whoever had it before.
            foreach (var c in Commands.Where(c => !overridden.Contains(c.Command)))
                foreach (var s in keys[c.Command]) map.Bind(s, c.Command);
            foreach (var c in overridden)
                foreach (var s in keys[c]) map.Bind(s, c);
            return map;
        }

        private void Bind(Shortcut shortcut, ShortcutCommand command)
        {
            if (_byKey.TryGetValue(shortcut, out var previous))
                _byCommand[previous].Remove(shortcut);
            _byKey[shortcut] = command;
            if (!_byCommand.TryGetValue(command, out var list)) _byCommand[command] = list = new List<Shortcut>();
            if (!list.Contains(shortcut)) list.Add(shortcut);
        }

        private static List<Shortcut> Parse(string text, List<string>? problems)
        {
            var list = new List<Shortcut>();
            foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Shortcut.TryParse(part, out var s)) list.Add(s);
                else problems?.Add($"\"{part}\" isn't a key DORI understands.");
            }
            return list;
        }
    }
}
