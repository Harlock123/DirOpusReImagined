using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class ProgressFormatTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1.0 KB")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(1024L * 1024, "1.0 MB")]
    [InlineData(1468006L, "1.4 MB")]
    [InlineData(5L * 1024 * 1024 * 1024, "5.0 GB")]
    [InlineData(3L * 1024 * 1024 * 1024 * 1024, "3.0 TB")]
    [InlineData(2048L * 1024 * 1024 * 1024 * 1024, "2048.0 TB")] // TB is the largest unit
    public void Bytes(long bytes, string expected)
        => Assert.Equal(expected, Invariant.Run(() => ProgressFormat.Bytes(bytes)));

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(3, "3s")]
    [InlineData(59, "59s")]
    [InlineData(125, "2m 5s")]
    [InlineData(3600 + 12 * 60 + 30, "1h 12m")]
    [InlineData(26 * 3600, "26h 0m")]
    public void Eta(int seconds, string expected)
        => Assert.Equal(expected, ProgressFormat.Eta(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Stats_with_total_speed_and_eta()
    {
        var p = new TransferProgress("f", 1, 1, 1024, 4096, 1024);
        Assert.Equal("1.0 KB / 4.0 KB   1.0 KB/s   ETA 3s", Invariant.Run(() => ProgressFormat.Stats(p)));
    }

    [Fact]
    public void Stats_without_total_shows_bytes_done_only()
    {
        var p = new TransferProgress("f", 1, 1, 2048, 0, 0);
        Assert.Equal("2.0 KB", Invariant.Run(() => ProgressFormat.Stats(p)));
    }

    [Fact]
    public void Stats_with_nothing_known_is_empty()
        => Assert.Equal("", ProgressFormat.Stats(new TransferProgress("f", 0, 0, 0, 0, 0)));
}

public class TransferProgressTests
{
    [Fact]
    public void Fraction_and_eta()
    {
        var p = new TransferProgress("f", 1, 2, 250, 1000, 50);
        Assert.True(p.HasTotal);
        Assert.Equal(0.25, p.Fraction, 6);
        Assert.Equal(TimeSpan.FromSeconds(15), p.Eta);
    }

    [Fact]
    public void Unknown_total_gives_zero_fraction_and_no_eta()
    {
        var p = new TransferProgress("f", 1, 2, 250, 0, 50);
        Assert.False(p.HasTotal);
        Assert.Equal(0, p.Fraction);
        Assert.Null(p.Eta);
    }

    [Fact]
    public void Overshoot_is_clamped_and_eta_is_zero()
    {
        var p = new TransferProgress("f", 1, 1, 1500, 1000, 10);
        Assert.Equal(1, p.Fraction);
        Assert.Equal(TimeSpan.Zero, p.Eta);
    }
}

public class TransferItemTests
{
    [Fact]
    public void Is_a_value_record()
    {
        var a = new TransferItem("/s/a", "/d", "/d/a", false);
        Assert.Equal(new TransferItem("/s/a", "/d", "/d/a", false), a);
        Assert.NotEqual(a with { IsDirectory = true }, a);
        Assert.Equal("/d/b", (a with { TargetPath = "/d/b" }).TargetPath);
    }
}

public class SortSpecTests
{
    [Fact]
    public void Default_is_name_ascending()
        => Assert.Equal(new SortSpec(SortKey.Name, true), SortSpec.Default);

    [Fact]
    public void Toward_same_key_toggles_direction()
    {
        Assert.Equal(new SortSpec(SortKey.Name, false), SortSpec.Default.Toward(SortKey.Name));
        Assert.Equal(new SortSpec(SortKey.Size, true), new SortSpec(SortKey.Size, false).Toward(SortKey.Size));
    }

    [Theory]
    [InlineData(SortKey.Size, false)]
    [InlineData(SortKey.Date, false)]
    [InlineData(SortKey.Type, false)]
    public void Toward_new_key_defaults_to_descending_except_name(SortKey key, bool ascending)
        => Assert.Equal(new SortSpec(key, ascending), SortSpec.Default.Toward(key));

    [Fact]
    public void Toward_name_from_another_key_is_ascending()
        => Assert.Equal(new SortSpec(SortKey.Name, true), new SortSpec(SortKey.Date, false).Toward(SortKey.Name));
}

public class PanelTabTests
{
    [Fact]
    public void New_tab_has_default_state()
    {
        var t = new PanelTab("/home/u");
        Assert.Equal("/home/u", t.Path);
        Assert.Empty(t.History);
        Assert.Empty(t.Forward);
        Assert.Equal(SortSpec.Default, t.Sort);
        Assert.Equal("", t.FilterText);
        Assert.Equal(-1, t.CursorIndex);
        Assert.Equal("", new PanelTab(null!).Path);
    }

    [Theory]
    [InlineData("/home/user/Documents", "Documents")]
    [InlineData("/home/user/Documents/", "Documents")]
    [InlineData(@"C:\Users\me\Desktop", "Desktop")]
    [InlineData(@"C:\Users\me\Desktop\", "Desktop")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData("cloud://gdrive/photos/2024", "2024")]
    [InlineData("archive:///home/u/a.zip!/docs/sub", "sub")]
    [InlineData("archive:///home/u/a.zip!/docs", "docs")]
    [InlineData("plain", "plain")]
    public void Title_is_the_final_segment(string path, string expected)
        => Assert.Equal(expected, new PanelTab(path).Title);

    [Fact]
    public void Title_of_an_archive_root_is_the_archive_file_name()
        => Assert.Equal("a.zip", new PanelTab("archive:///home/u/a.zip!/").Title);
}
