using System.Xml.Linq;

namespace DirOpusReImagined.Tests;

public class SavedFiltersTests
{
    [Theory]
    [InlineData("*.cs", "Main.cs", true)]
    [InlineData("*.cs", "Main.css", false)]
    [InlineData("*.cs;*.axaml", "App.axaml", true)]
    [InlineData("*.cs ; *.axaml", "App.AXAML", true)]   // spaces and case don't matter
    [InlineData("*.cs;*.axaml", "readme.md", false)]
    [InlineData("report", "Q3 report.docx", true)]       // no wildcard: contains
    [InlineData("jpg;png", "logo.png", true)]
    [InlineData("src/*.cs", "src/app/x.cs", true)]        // flat view names include folders
    [InlineData(";;", "anything", true)]                  // nothing to match against: everything passes
    public void Filter_text_matches_any_of_its_patterns(string filter, string name, bool expected)
        => Assert.Equal(expected, SavedFilters.Matcher(filter)(name));

    [Fact]
    public void Saved_filters_read_back_what_was_written()
    {
        var root = new XElement("Settings", new XElement("Theme", "Nord"));
        SavedFilters.Write(root, new[] { new SavedFilter("Images", "*.jpg;*.png"), new SavedFilter("*.log", "*.log") });
        var back = SavedFilters.Read(root);
        Assert.Equal(new[] { ("Images", "*.jpg;*.png"), ("*.log", "*.log") }, back.Select(f => (f.Name, f.Pattern)));
        Assert.Equal("Nord", (string?)root.Element("Theme"));
    }

    [Fact]
    public void A_filter_without_a_name_is_named_by_its_pattern_and_empty_ones_dropped()
    {
        var root = XElement.Parse("<Settings><SavedFilters><Filter>*.txt</Filter><Filter name='Nothing'> </Filter></SavedFilters></Settings>");
        var f = Assert.Single(SavedFilters.Read(root));
        Assert.Equal("*.txt", f.Name);
        Assert.Equal("*.txt", f.MenuLabel);
    }

    [Fact]
    public void A_named_filter_shows_its_pattern_too()
        => Assert.Equal("Images   *.jpg;*.png", new SavedFilter("Images", "*.jpg;*.png").MenuLabel);

    [Fact]
    public void Saving_under_an_existing_name_replaces_it()
    {
        var list = SavedFilters.Upsert(new[] { new SavedFilter("Images", "*.jpg") }, new SavedFilter("images", "*.jpg;*.webp"));
        Assert.Equal("*.jpg;*.webp", Assert.Single(list).Pattern);
    }

    [Fact]
    public void Removing_the_last_filter_removes_the_section()
    {
        var root = new XElement("Settings");
        SavedFilters.Write(root, new[] { new SavedFilter("a", "a") });
        SavedFilters.Write(root, Array.Empty<SavedFilter>());
        Assert.Null(root.Element(SavedFilters.ElementName));
    }
}
