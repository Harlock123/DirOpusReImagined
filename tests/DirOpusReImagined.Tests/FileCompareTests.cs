namespace DirOpusReImagined.Tests;

public class FileCompareTests
{
    private static List<string> L(params string[] lines) => lines.ToList();

    private static string Shape(IEnumerable<DiffRow> rows)
        => string.Concat(rows.Select(r => r.Kind switch
        {
            DiffKind.Same => '=', DiffKind.Changed => '~', DiffKind.Removed => '-', _ => '+',
        }));

    [Theory]
    [InlineData("a\nb\nc", new[] { "a", "b", "c" })]
    [InlineData("a\r\nb\r\n", new[] { "a", "b" })]   // final line break adds nothing
    [InlineData("a\rb", new[] { "a", "b" })]
    [InlineData("a\n\nb", new[] { "a", "", "b" })]
    [InlineData("", new string[0])]
    public void Lines_split_on_any_line_ending(string text, string[] expected)
        => Assert.Equal(expected, TextDiff.SplitLines(text));

    [Fact]
    public void Identical_texts_are_all_the_same()
    {
        var rows = TextDiff.Compare(L("a", "b"), L("a", "b"));
        Assert.Equal("==", Shape(rows));
        Assert.Equal("The files are identical.", TextDiff.Summary(rows, false));
        Assert.Empty(TextDiff.ChangeStarts(rows));
    }

    [Fact]
    public void A_changed_line_pairs_up_side_by_side()
    {
        var rows = TextDiff.Compare(L("a", "b", "c"), L("a", "B", "c"));
        Assert.Equal("=~=", Shape(rows));
        Assert.Equal((2, "b", 2, "B"), (rows[1].LeftNo!.Value, rows[1].LeftText, rows[1].RightNo!.Value, rows[1].RightText));
    }

    [Fact]
    public void Added_and_removed_lines_leave_the_other_side_blank()
    {
        var rows = TextDiff.Compare(L("a", "gone", "b"), L("a", "b", "new"));
        Assert.Equal("=-=+", Shape(rows));
        Assert.Null(rows[1].RightNo);
        Assert.Equal("", rows[1].RightText);
        Assert.Null(rows[3].LeftNo);
        Assert.Equal(3, rows[3].RightNo);
        Assert.Equal("1 removed, 1 added in 2 places.", TextDiff.Summary(rows, false));
        Assert.Equal(new[] { 1, 3 }, TextDiff.ChangeStarts(rows));
    }

    [Fact]
    public void One_side_empty()
    {
        Assert.Equal("++", Shape(TextDiff.Compare(L(), L("x", "y"))));
        Assert.Equal("--", Shape(TextDiff.Compare(L("x", "y"), L())));
        Assert.Empty(TextDiff.Compare(L(), L()));
    }

    [Fact]
    public void Whitespace_can_be_ignored()
    {
        var left = L("if (x) {", "\treturn 1;", "}");
        var right = L("if (x)  {", "    return 1;", "}");
        Assert.Equal("~~=", Shape(TextDiff.Compare(left, right)));
        var rows = TextDiff.Compare(left, right, ignoreWhitespace: true);
        Assert.Equal("===", Shape(rows));
        Assert.Equal("The files match, apart from whitespace.", TextDiff.Summary(rows, true));
        Assert.Equal("    return 1;", rows[1].RightText);   // shown as it is, only compared loosely
    }

    [Fact]
    public void Random_edits_give_a_minimal_comparison_that_rebuilds_both_sides()
    {
        var rng = new Random(1234);
        for (int round = 0; round < 300; round++)
        {
            var a = Enumerable.Range(0, rng.Next(0, 30)).Select(_ => ((char)('a' + rng.Next(5))).ToString()).ToList();
            var b = Enumerable.Range(0, rng.Next(0, 30)).Select(_ => ((char)('a' + rng.Next(5))).ToString()).ToList();
            var rows = TextDiff.Compare(a, b);

            Assert.Equal(a, rows.Where(r => r.LeftNo != null).Select(r => r.LeftText));
            Assert.Equal(b, rows.Where(r => r.RightNo != null).Select(r => r.RightText));
            Assert.Equal(Enumerable.Range(1, a.Count), rows.Where(r => r.LeftNo != null).Select(r => r.LeftNo!.Value));

            // Shortest: the lines kept unchanged are as many as the longest common subsequence.
            Assert.Equal(Lcs(a, b), rows.Count(r => r.Kind == DiffKind.Same));
        }
    }

    private static int Lcs(List<string> a, List<string> b)
    {
        var t = new int[a.Count + 1, b.Count + 1];
        for (int i = 1; i <= a.Count; i++)
            for (int j = 1; j <= b.Count; j++)
                t[i, j] = a[i - 1] == b[j - 1] ? t[i - 1, j - 1] + 1 : Math.Max(t[i - 1, j], t[i, j - 1]);
        return t[a.Count, b.Count];
    }

    [Fact]
    public void Large_files_with_a_small_change_are_quick()
    {
        var a = Enumerable.Range(0, 200_000).Select(i => "line " + i).ToList();
        var b = a.ToList();
        b[100_000] = "changed";
        b.Insert(150_000, "inserted");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = TextDiff.Compare(a, b);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms");
        Assert.Equal(2, TextDiff.ChangeStarts(rows).Count);
    }

    [Fact]
    public void Wholly_different_files_still_finish()
    {
        var a = Enumerable.Range(0, 6000).Select(i => "a" + i).ToList();
        var b = Enumerable.Range(0, 6000).Select(i => "b" + i).ToList();
        var rows = TextDiff.Compare(a, b);
        Assert.Equal(6000, rows.Count(r => r.Kind == DiffKind.Changed));
    }

    [Fact]
    public void Binary_is_spotted_by_a_nul_byte()
    {
        Assert.True(TextDiff.LooksBinary(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00 }));
        Assert.False(TextDiff.LooksBinary("plain text\n"u8));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 }, new byte[] { 1, 2, 3 }, null)]
    [InlineData(new byte[] { 1, 2, 3 }, new byte[] { 1, 9, 3 }, 1L)]
    [InlineData(new byte[] { 1, 2 }, new byte[] { 1, 2, 3 }, 2L)]     // one is longer
    [InlineData(new byte[0], new byte[0], null)]
    public void First_differing_byte(byte[] a, byte[] b, long? expected)
        => Assert.Equal(expected, TextDiff.FirstDifference(new MemoryStream(a), new MemoryStream(b)));

    [Fact]
    public void First_difference_past_the_first_buffer()
    {
        var a = new byte[200_000];
        var b = (byte[])a.Clone();
        b[150_001] = 7;
        Assert.Equal(150_001L, TextDiff.FirstDifference(new MemoryStream(a), new MemoryStream(b)));
    }

    [Fact]
    public void Two_files_selected_in_one_panel_are_compared()
        => Assert.Equal(((true, "a.txt"), (true, "b.txt")),
                        TextDiff.PickFiles(new[] { "a.txt", "b.txt" }, "c.txt", new[] { "z.txt" }, null));

    [Fact]
    public void Otherwise_one_file_from_each_panel()
    {
        Assert.Equal(((true, "mine.txt"), (false, "theirs.txt")),
                     TextDiff.PickFiles(new[] { "mine.txt" }, null, new string[0], "theirs.txt"));
        Assert.Equal(((true, "cursor.txt"), (false, "sel.txt")),
                     TextDiff.PickFiles(new string[0], "cursor.txt", new[] { "sel.txt", "other.txt" }, "x"));
    }

    [Fact]
    public void Nothing_suitable_picks_nothing()
    {
        Assert.Null(TextDiff.PickFiles(new string[0], null, new[] { "x" }, null));
        Assert.Null(TextDiff.PickFiles(new[] { "a" }, null, new string[0], null));
        Assert.Null(TextDiff.PickFiles(new[] { "a", "b", "c" }, null, new[] { "x" }, null));   // three is ambiguous
    }
}
