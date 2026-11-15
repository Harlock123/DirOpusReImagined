namespace DirOpusReImagined.Tests;

public class CommandPaletteTests
{
    private static readonly List<PaletteItem> Items = new()
    {
        new("Command", "Copy to the other panel", "F5", () => { }),
        new("Command", "Flat view on / off", "Ctrl+Shift+F", () => { }),
        new("Command", "Find duplicate files", "", () => { }),
        new("Button", "VSCODE here", "", () => { }),
        new("Layout", "Photo sorting", "", () => { }),
        new("Filter", "Images   *.jpg;*.png", "", () => { }),
    };

    private static List<string> Titles(string query) => CommandPalette.Rank(Items, query).Select(i => i.Title).ToList();

    [Fact]
    public void Nothing_typed_lists_everything_in_order()
        => Assert.Equal(Items.Select(i => i.Title), Titles(""));

    [Fact]
    public void Every_word_typed_must_appear()
    {
        Assert.Equal(new[] { "Find duplicate files" }, Titles("find dup"));
        Assert.Empty(Titles("find banana"));
    }

    [Fact]
    public void A_title_starting_with_the_text_comes_first()
    {
        var ranked = Titles("fl");
        Assert.Equal("Flat view on / off", ranked[0]);
    }

    [Fact]
    public void Word_starts_beat_matches_inside_words()
    {
        var items = new List<PaletteItem>
        {
            new("Command", "Reflow text", "", () => { }),   // "flow" inside a word
            new("Command", "Data flow", "", () => { }),     // "flow" starts a word
        };
        Assert.Equal("Data flow", CommandPalette.Rank(items, "flow")[0].Title);
    }

    [Fact]
    public void The_group_and_keys_can_be_searched_too()
    {
        Assert.Equal(new[] { "Photo sorting" }, Titles("layout"));
        Assert.Equal(new[] { "Copy to the other panel" }, Titles("F5"));
    }

    [Fact]
    public void Case_is_ignored()
        => Assert.Equal(new[] { "VSCODE here" }, Titles("vscode"));
}
