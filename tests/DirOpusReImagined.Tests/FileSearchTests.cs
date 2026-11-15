namespace DirOpusReImagined.Tests;

public class FileSearchTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Theory]
    [InlineData("jpg", "holiday.JPG", true)]      // substring, case-insensitive by default
    [InlineData("day", "holiday.jpg", true)]
    [InlineData("xyz", "holiday.jpg", false)]
    [InlineData("*.jpg", "holiday.jpg", true)]    // wildcard matches the whole name
    [InlineData("*.jpg", "holiday.jpg.bak", false)]
    [InlineData("*.JPG", "holiday.jpg", true)]
    [InlineData("report?", "report1", true)]
    [InlineData("report?", "report12", false)]
    [InlineData("report?", "report", false)]
    [InlineData("proj*", "project.txt", true)]
    [InlineData("proj*", "myproject.txt", false)]
    [InlineData("a.b", "aXb", false)]             // regex metacharacters are literal
    [InlineData("(1)", "file (1).txt", true)]
    [InlineData("[x]*", "[x]y", true)]
    public void MakeNameMatcher_wildcard_vs_substring(string pattern, string name, bool expected)
        => Assert.Equal(expected, FileSearch.MakeNameMatcher(pattern).IsMatch(name));

    [Fact]
    public void MakeNameMatcher_honours_match_case()
    {
        Assert.DoesNotMatch(FileSearch.MakeNameMatcher("JPG", matchCase: true), "a.jpg");
        Assert.Matches(FileSearch.MakeNameMatcher("jpg", matchCase: true), "a.jpg");
        Assert.DoesNotMatch(FileSearch.MakeNameMatcher("*.JPG", matchCase: true), "a.jpg");
    }

    private void BuildTree()
    {
        _t.File("a.jpg", "1");
        _t.File("b.txt", "22");
        _t.File("photos/c.jpg", "333");
        _t.File("photos/2024/d.JPG", "4444");
        _t.Dir("jpg-folder");
    }

    private List<SearchHit> Run(string pattern, SearchOptions opt)
    {
        var hits = new List<SearchHit>();
        FileSearch.Search(_t.Path, pattern, opt, hits.Add, CancellationToken.None);
        return hits;
    }

    [Fact]
    public void Search_recurses_and_reports_folder_and_size()
    {
        BuildTree();
        var hits = Run("*.jpg", new SearchOptions(MatchCase: false, IncludeFolders: false));

        Assert.Equal(new[] { "a.jpg", "c.jpg", "d.JPG" }, hits.Select(h => h.Name).OrderBy(n => n, StringComparer.Ordinal));
        var d = hits.Single(h => h.Name == "d.JPG");
        Assert.Equal(_t.Combine("photos/2024"), d.Folder);
        Assert.Equal(_t.Combine("photos/2024/d.JPG"), d.FullPath);
        Assert.Equal(4, d.Size);
        Assert.False(d.IsDir);
    }

    [Fact]
    public void Search_with_match_case_excludes_differently_cased_names()
    {
        BuildTree();
        var hits = Run("*.jpg", new SearchOptions(MatchCase: true, IncludeFolders: false));
        Assert.Equal(new[] { "a.jpg", "c.jpg" }, hits.Select(h => h.Name).OrderBy(n => n));
    }

    [Fact]
    public void Search_includes_folders_only_when_asked()
    {
        BuildTree();
        Assert.DoesNotContain(Run("jpg", new SearchOptions(false, false)), h => h.IsDir);
        var withFolders = Run("jpg", new SearchOptions(false, true));
        var folder = Assert.Single(withFolders, h => h.IsDir);
        Assert.Equal("jpg-folder", folder.Name);
    }

    [Fact]
    public void Search_with_blank_pattern_or_root_finds_nothing()
    {
        BuildTree();
        Assert.Empty(Run("  ", new SearchOptions(false, true)));
        var hits = new List<SearchHit>();
        FileSearch.Search("", "a", new SearchOptions(false, true), hits.Add, CancellationToken.None);
        Assert.Empty(hits);
    }

    [Fact]
    public void Search_skips_a_missing_root_instead_of_throwing()
    {
        var hits = new List<SearchHit>();
        FileSearch.Search(_t.Combine("does-not-exist"), "a", new SearchOptions(false, true), hits.Add, CancellationToken.None);
        Assert.Empty(hits);
    }

    [Fact]
    public void Search_honours_cancellation()
    {
        BuildTree();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            FileSearch.Search(_t.Path, "a", new SearchOptions(false, true), _ => { }, cts.Token));
    }

    [Fact]
    public void Search_reports_each_visited_folder_as_progress()
    {
        BuildTree();
        var visited = new List<string>();
        FileSearch.Search(_t.Path, "zzz", new SearchOptions(false, true), _ => { }, CancellationToken.None,
            new SyncProgress(visited.Add));
        Assert.Contains(_t.Path, visited);
        Assert.Contains(_t.Combine("photos/2024"), visited);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
