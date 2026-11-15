using System.Xml.Linq;

namespace DirOpusReImagined.Tests;

public class RecentFoldersTests
{
    private static RecentFolders Make(bool ignoreCase = false) => new(ignoreCase);

    [Fact]
    public void Newest_visit_comes_first_without_duplicates()
    {
        var r = Make();
        r.Visit("/a");
        r.Visit("/b");
        r.Visit("/c");
        r.Visit("/a");
        Assert.Equal(new[] { "/a", "/c", "/b" }, r.Items);
    }

    [Fact]
    public void A_trailing_separator_is_the_same_folder()
    {
        var r = Make();
        r.Visit("/home/me/");
        r.Visit("/home/me");
        Assert.Equal(new[] { "/home/me" }, r.Items);
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("C:", "C:\\")]
    [InlineData("D:\\Photos\\", "D:\\Photos")]
    public void Roots_are_kept_whole(string visited, string stored)
    {
        var r = Make();
        r.Visit(visited);
        Assert.Equal(stored, Assert.Single(r.Items));
    }

    [Theory]
    [InlineData("archive:///home/me/a.zip!/")]
    [InlineData("cloud://gdrive/")]
    [InlineData("cloud://gdrive/Photos")]
    public void Provider_uris_are_kept_exactly(string uri)
    {
        var r = Make();
        r.Visit(uri);
        Assert.Equal(uri, Assert.Single(r.Items));
    }

    [Fact]
    public void Case_is_ignored_only_where_the_file_system_ignores_it()
    {
        var sensitive = Make(ignoreCase: false);
        sensitive.Visit("/Photos");
        sensitive.Visit("/photos");
        Assert.Equal(2, sensitive.Items.Count);

        var insensitive = Make(ignoreCase: true);
        insensitive.Visit("C:\\Photos");
        insensitive.Visit("c:\\photos");
        Assert.Equal(new[] { "c:\\photos" }, insensitive.Items);
    }

    [Fact]
    public void Keeps_only_the_newest_entries()
    {
        var r = Make();
        for (int i = 0; i < RecentFolders.MaxItems + 10; i++) r.Visit($"/f{i}");
        Assert.Equal(RecentFolders.MaxItems, r.Items.Count);
        Assert.Equal($"/f{RecentFolders.MaxItems + 9}", r.Items[0]);
    }

    [Fact]
    public void Except_leaves_out_the_current_folder()
    {
        var r = Make();
        r.Visit("/a");
        r.Visit("/b");
        Assert.Equal(new[] { "/a" }, r.Except("/b/"));
        Assert.Equal(new[] { "/b", "/a" }, r.Except(null));
    }

    [Fact]
    public void Remove_and_clear()
    {
        var r = Make();
        r.Visit("/a");
        r.Visit("/b");
        Assert.True(r.Remove("/a/"));
        Assert.False(r.Remove("/nope"));
        Assert.Equal(new[] { "/b" }, r.Items);
        r.Clear();
        Assert.Empty(r.Items);
    }

    [Fact]
    public void Saved_folders_go_below_ones_visited_this_session()
    {
        var r = Make();
        r.Visit("/started-here");
        r.AddOlder(new[] { "/saved1", "/started-here", "", "/saved2" });
        Assert.Equal(new[] { "/started-here", "/saved1", "/saved2" }, r.Items);
    }

    [Fact]
    public void Round_trips_through_the_settings_file()
    {
        var r = Make();
        r.Visit("/a");
        r.Visit("cloud://gdrive/Photos");
        var settings = new XElement("Settings", new XElement("Tabs"), r.ToXml());

        var restored = Make();
        restored.AddOlder(RecentFolders.FromXml(XElement.Parse(settings.ToString())));
        Assert.Equal(r.Items, restored.Items);
    }

    [Fact]
    public void A_settings_file_without_the_list_gives_nothing()
    {
        Assert.Empty(RecentFolders.FromXml(new XElement("Settings")));
        Assert.Empty(RecentFolders.FromXml(null));
    }

    [Fact]
    public void Menu_label_shows_the_name_then_where_it_is_with_home_as_tilde()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string photos = Path.Combine(home, "Pictures", "2024");
        Assert.Equal($"2024   ~{Path.DirectorySeparatorChar}Pictures", MainWindow.MenuLabel(photos));
    }

    [Theory]
    [InlineData("/", "/")]
    [InlineData("/srv/data", "data   /srv")]
    [InlineData("cloud://gdrive/Photos", "Photos   cloud://gdrive/Photos")]
    [InlineData("archive:///tmp/a.zip!/docs", "docs   archive:///tmp/a.zip!/docs")]
    public void Menu_labels(string path, string expected)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith('/') && path.Length > 1) return;   // a Unix path
        Assert.Equal(expected, MainWindow.MenuLabel(path));
    }

    [Fact]
    public void Long_paths_are_shortened_in_the_middle()
    {
        string longPath = "/very/long/path/" + string.Join("/", Enumerable.Range(0, 20).Select(i => $"level{i}"));
        string s = MainWindow.Shorten(longPath);
        Assert.Equal(44, s.Length);
        Assert.StartsWith("/very/long/pa", s);
        Assert.EndsWith("level19", s);
        Assert.Contains("…", s);
        Assert.Equal("/short/path", MainWindow.Shorten("/short/path"));
    }
}
