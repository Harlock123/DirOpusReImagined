using System.IO.Compression;
using System.Text;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

public class FileContentSearchTests : IDisposable
{
    private readonly TempDir _t = new();

    public FileContentSearchTests()
    {
        _t.File("notes.txt", "first line\nthe Needle is here\nanother needle\n");
        _t.File("readme.md", "nothing to see\n");
        _t.File("src/code.cs", "class A { int needle = 1; }\n");
        _t.File("src/deep/more.txt", "no match\nstill none\nneedle on line 3\n");
    }

    public void Dispose() => _t.Dispose();

    private List<SearchHit> Run(string pattern, SearchOptions opt, SearchStats? stats = null, string? root = null)
    {
        var hits = new List<SearchHit>();
        FileSearch.Search(root ?? _t.Path, pattern, opt, hits.Add, CancellationToken.None, stats: stats);
        return hits;
    }

    private static SearchOptions Text(string text, bool matchCase = false, bool regex = false)
        => new(matchCase, IncludeFolders: true, ContainingText: text, TextIsRegex: regex);

    private static string[] Names(IEnumerable<SearchHit> hits) => hits.Select(h => h.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void Empty_name_pattern_searches_every_file()
        => Assert.Equal(new[] { "code.cs", "more.txt", "notes.txt" }, Names(Run("", Text("needle"))));

    [Fact]
    public void Name_pattern_narrows_which_files_are_read()
        => Assert.Equal(new[] { "more.txt", "notes.txt" }, Names(Run("*.txt", Text("needle"))));

    [Fact]
    public void Reports_the_first_matching_line_and_how_many_lines_match()
    {
        var hit = Assert.Single(Run("notes.txt", Text("needle")));
        Assert.Equal(2, hit.Line);
        Assert.Equal("the Needle is here", hit.Snippet);
        Assert.Equal(2, hit.MatchCount);
        Assert.Equal("Line 2: the Needle is here  · 2 matching lines", hit.MatchSummary);
        Assert.Equal(_t.Path, hit.Folder);
    }

    [Fact]
    public void Match_case_applies_to_the_text()
    {
        var hit = Assert.Single(Run("notes.txt", Text("Needle", matchCase: true)));
        Assert.Equal(1, hit.MatchCount);
        Assert.Empty(Run("readme.md", Text("NOTHING", matchCase: true)));
    }

    [Fact]
    public void Literal_text_is_not_treated_as_a_regex()
    {
        _t.File("special.txt", "price: $5.00 (approx)\n");
        Assert.Single(Run("special.txt", Text("$5.00 (approx)")));
        Assert.Empty(Run("special.txt", Text("$5x00")));
    }

    [Fact]
    public void Regex_text_is_supported()
    {
        var hit = Assert.Single(Run("code.cs", Text(@"int\s+\w+\s*=\s*\d", regex: true)));
        Assert.Equal(1, hit.Line);
    }

    [Fact]
    public void An_invalid_regex_is_an_argument_error()
        => Assert.ThrowsAny<ArgumentException>(() => FileSearch.MakeTextMatcher("(unclosed", isRegex: true, matchCase: false));

    [Fact]
    public void Folders_are_not_reported_in_a_text_search()
        => Assert.DoesNotContain(Run("src", Text("needle")), h => h.IsDir);

    [Fact]
    public void Name_only_search_still_works_and_has_no_snippet()
    {
        var hits = Run("*.md", new SearchOptions(false, false));
        var hit = Assert.Single(hits);
        Assert.Null(hit.Snippet);
        Assert.Null(hit.MatchSummary);
    }

    [Fact]
    public void Binary_files_are_skipped_and_counted()
    {
        File.WriteAllBytes(_t.Combine("blob.bin"), new byte[] { 0x6E, 0x65, 0x65, 0x64, 0x6C, 0x65, 0x00, 0x01 });
        var stats = new SearchStats();
        Assert.Empty(Run("blob.bin", Text("needle"), stats));
        Assert.Equal(1, stats.SkippedBinary);
        Assert.Equal(0, stats.FilesRead);
    }

    [Fact]
    public void Utf16_text_with_a_bom_is_read_not_treated_as_binary()
    {
        File.WriteAllText(_t.Combine("wide.txt"), "alpha\nfind me here\n", Encoding.Unicode);
        var hit = Assert.Single(Run("wide.txt", Text("find me")));
        Assert.Equal(2, hit.Line);
    }

    [Fact]
    public void Utf8_text_with_non_ascii_characters_matches()
    {
        File.WriteAllText(_t.Combine("intl.txt"), "Grüße aus Köln\n", new UTF8Encoding(true));
        var hit = Assert.Single(Run("intl.txt", Text("köln")));
        Assert.Equal("Grüße aus Köln", hit.Snippet);
    }

    [Fact]
    public void A_match_beyond_the_sniffed_head_is_found()
    {
        // The first 8 KB are read to check for binary data; the rest must still be searched.
        var sb = new StringBuilder();
        for (int i = 0; i < 2000; i++) sb.Append("filler line number ").Append(i).Append('\n');
        sb.Append("late needle\n");
        File.WriteAllText(_t.Combine("big.log"), sb.ToString());

        var hit = Assert.Single(Run("big.log", Text("late needle")));
        Assert.Equal(2001, hit.Line);
    }

    [Fact]
    public void Files_over_the_size_limit_are_skipped_and_counted()
    {
        var stats = new SearchStats();
        var opt = Text("needle") with { MaxFileBytes = 10 };
        Assert.Empty(Run("notes.txt", opt, stats));
        Assert.Equal(1, stats.SkippedTooLarge);
    }

    [Fact]
    public void Stats_count_files_read()
    {
        var stats = new SearchStats();
        Run("", Text("needle"), stats);
        Assert.Equal(4, stats.FilesRead);
        Assert.Equal("4 file(s) read", stats.Describe());
    }

    [Fact]
    public void Files_inside_an_archive_are_searched()
    {
        var zip = _t.Combine("pack.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(z.CreateEntry("inner/doc.txt").Open());
            w.Write("one\ntwo needle\n");
        }

        var hit = Assert.Single(Run("", Text("needle"), root: ArchivePath.RootUriFor(zip)));
        Assert.Equal("doc.txt", hit.Name);
        Assert.Equal(2, hit.Line);
    }

    [Fact]
    public void Cancellation_stops_the_search()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            FileSearch.Search(_t.Path, "", Text("needle"), _ => { }, cts.Token));
    }

    [Theory]
    [InlineData(new byte[] { 0x61, 0x62, 0x63 }, false)]
    [InlineData(new byte[] { 0x61, 0x00, 0x63 }, true)]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x61, 0x00 }, false)]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x61 }, false)]
    [InlineData(new byte[] { 0x00, 0x00, 0xFE, 0xFF }, false)]
    [InlineData(new byte[0], false)]
    public void LooksBinary_detects_nul_bytes_except_in_utf16_and_utf32(byte[] head, bool expected)
        => Assert.Equal(expected, FileSearch.LooksBinary(head));

    [Fact]
    public void Snippet_of_a_long_line_shows_the_match_with_ellipses()
    {
        string line = new string('a', 300) + "NEEDLE" + new string('b', 300);
        string s = FileSearch.MakeSnippet(line, 300, 6);
        Assert.Contains("NEEDLE", s);
        Assert.StartsWith("…", s);
        Assert.EndsWith("…", s);
        Assert.True(s.Length <= 162);
    }

    [Fact]
    public void Snippet_of_a_short_line_is_the_trimmed_line()
        => Assert.Equal("int x = needle;", FileSearch.MakeSnippet("\t  int x = needle;  ", 11, 6));
}
