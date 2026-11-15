using System.Xml.Linq;
using Avalonia.Input;

namespace DirOpusReImagined.Tests;

public class KeyMapTests
{
    private static KeyMap Map(string shortcutsXml, out List<string> problems)
        => KeyMap.FromXml(XElement.Parse($"<Settings><Shortcuts>{shortcutsXml}</Shortcuts></Settings>"), out problems);

    private static ShortcutCommand? Run(KeyMap map, Key key, KeyModifiers mods = KeyModifiers.None)
        => map.TryGet(key, mods, out var c) ? c : null;

    [Theory]
    [InlineData("F5", Key.F5, false, false, false)]
    [InlineData("ctrl+shift+f", Key.F, true, true, false)]
    [InlineData("Cmd+R", Key.R, true, false, false)]
    [InlineData("Alt + PgDn", Key.PageDown, false, false, true)]
    [InlineData("Option+Left", Key.Left, false, false, true)]
    [InlineData("Shift+8", Key.D8, false, true, false)]
    [InlineData("Backspace", Key.Back, false, false, false)]
    [InlineData("NumPlus", Key.Add, false, false, false)]
    public void Keys_are_read_in_the_usual_forms(string text, Key key, bool ctrl, bool shift, bool alt)
    {
        Assert.True(Shortcut.TryParse(text, out var s));
        Assert.Equal(new Shortcut(key, ctrl, shift, alt), s);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+F")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("42")]
    public void Nonsense_isnt_a_key(string text) => Assert.False(Shortcut.TryParse(text, out _));

    [Fact]
    public void A_shortcut_reads_back_what_it_writes()
    {
        var s = new Shortcut(Key.PageDown, true, true, true);
        Assert.True(Shortcut.TryParse(s.ToString(), out var back));
        Assert.Equal(s, back);
    }

    [Theory]
    [InlineData("Ctrl+Shift+F", false, "Ctrl+Shift+F")]
    [InlineData("Ctrl+Shift+F", true, "Cmd+Shift+F")]
    [InlineData("Alt+PageDown", true, "Option+PgDn")]
    [InlineData("Shift+Plus", false, "+")]
    [InlineData("Shift+8", false, "*")]
    [InlineData("NumMinus", false, "Num -")]
    public void Shortcuts_display_as_people_know_them(string text, bool mac, string expected)
    {
        Shortcut.TryParse(text, out var s);
        Assert.Equal(expected, s.Display(mac));
    }

    [Fact]
    public void The_defaults_match_what_DORI_always_did()
    {
        var map = KeyMap.FromXml(null, out var problems);
        Assert.Empty(problems);
        Assert.Equal(ShortcutCommand.Copy, Run(map, Key.F5));
        Assert.Equal(ShortcutCommand.Delete, Run(map, Key.Delete));
        Assert.Equal(ShortcutCommand.Refresh, Run(map, Key.R, KeyModifiers.Control));
        Assert.Equal(ShortcutCommand.Refresh, Run(map, Key.R, KeyModifiers.Meta));   // Cmd on macOS
        Assert.Equal(ShortcutCommand.PreviousTab, Run(map, Key.Tab, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(ShortcutCommand.SwitchPanel, Run(map, Key.Tab));
        Assert.Equal(ShortcutCommand.InvertSelection, Run(map, Key.D8, KeyModifiers.Shift));
        Assert.Null(Run(map, Key.Down));          // cursor movement isn't a command
        Assert.Null(Run(map, Key.F5, KeyModifiers.Alt));
        Assert.Equal("F8, Delete, Shift+Delete", map.DisplayFor(ShortcutCommand.Delete, mac: false));
    }

    [Fact]
    public void Every_command_has_a_known_default_and_title()
    {
        Assert.Equal(Enum.GetValues<ShortcutCommand>().Length, KeyMap.Commands.Count);
        Assert.All(KeyMap.Commands, c => Assert.False(string.IsNullOrWhiteSpace(c.Title)));
    }

    [Fact]
    public void Commands_the_panel_doesnt_handle_itself_are_panel_verbs()
    {
        // TaiDataGrid.RunShortcut handles these itself; every other command must share its name
        // with a GridVerb, or pressing its key would do nothing.
        var handledByPanel = new[]
        {
            ShortcutCommand.NewTab, ShortcutCommand.CloseTab, ShortcutCommand.NextTab, ShortcutCommand.PreviousTab,
            ShortcutCommand.SwitchPanel, ShortcutCommand.GoUp, ShortcutCommand.Forward, ShortcutCommand.SelectAll,
            ShortcutCommand.SelectByPattern, ShortcutCommand.DeselectByPattern, ShortcutCommand.InvertSelection,
        };
        foreach (var c in Enum.GetValues<ShortcutCommand>().Except(handledByPanel))
            Assert.True(Enum.TryParse<GridVerb>(c.ToString(), out _), $"{c} has no GridVerb");
    }

    [Fact]
    public void A_changed_command_replaces_its_keys()
    {
        var map = Map("<Shortcut command='Copy' keys='F5, Ctrl+Shift+C' />", out var problems);
        Assert.Empty(problems);
        Assert.Equal(ShortcutCommand.Copy, Run(map, Key.C, KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(ShortcutCommand.Copy, Run(map, Key.F5));
    }

    [Fact]
    public void An_empty_keys_list_unbinds()
    {
        var map = Map("<Shortcut command='delete' keys='' />", out _);
        Assert.Null(Run(map, Key.Delete));
        Assert.Null(Run(map, Key.F8));
        Assert.Equal("", map.DisplayFor(ShortcutCommand.Delete, false));
    }

    [Fact]
    public void A_key_given_to_another_command_is_taken_from_its_default()
    {
        var map = Map("<Shortcut command='CompareFiles' keys='F5' />", out _);
        Assert.Equal(ShortcutCommand.CompareFiles, Run(map, Key.F5));
        Assert.Empty(map.KeysFor(ShortcutCommand.Copy));
    }

    [Fact]
    public void Of_two_changes_claiming_a_key_the_later_wins()
    {
        var map = Map("<Shortcut command='Copy' keys='F4' /><Shortcut command='Move' keys='F4' />", out _);
        Assert.Equal(ShortcutCommand.Move, Run(map, Key.F4));
    }

    [Fact]
    public void Mistakes_are_skipped_and_reported()
    {
        var map = Map("<Shortcut command='Teleport' keys='F4' /><Shortcut command='Copy' keys='F5, Ctrl+Banana' />", out var problems);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("Teleport"));
        Assert.Contains(problems, p => p.Contains("Ctrl+Banana"));
        Assert.Equal(ShortcutCommand.Copy, Run(map, Key.F5));   // the good part still applies
        Assert.Null(Run(map, Key.F4));
    }

    [Fact]
    public void The_shipped_configurations_load_without_problems()
    {
        string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        foreach (var file in new[] { "Configuration.xml", "LINUXConfiguration.xml", "MACConfiguration .xml" })
        {
            KeyMap.FromXml(XDocument.Load(Path.Combine(repo, file)).Root, out var problems);
            Assert.Empty(problems);
        }
    }
}
