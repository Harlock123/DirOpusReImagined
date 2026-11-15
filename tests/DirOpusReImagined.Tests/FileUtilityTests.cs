namespace DirOpusReImagined.Tests;

public class FileUtilityTests
{
    [Theory]
    [InlineData("/home//user///docs", "/home/user/docs")]
    [InlineData(@"C:\\data\\\x", @"C:\data\x")]
    [InlineData(@"\\SERVER\SHARE\\FOLDER", @"\\SERVER\SHARE\FOLDER")] // UNC prefix kept
    [InlineData("//server/share//x", "//server/share/x")]
    [InlineData(@"a/\b", "a/b")]                                       // mixed separators collapse to the first
    [InlineData("", "")]
    [InlineData("plain", "plain")]
    [InlineData("cloud://gdrive/Photos//x", "cloud://gdrive/Photos/x")]                 // scheme kept
    [InlineData("archive:///home/me/a.zip!/docs//x", "archive:///home/me/a.zip!/docs/x")]
    [InlineData(@"archive://C:\\data\\a.zip!/x", @"archive://C:\data\a.zip!/x")]
    public void CollapseSeparators(string input, string expected)
        => Assert.Equal(expected, FileUtility.CollapseSeparators(input));

    [Fact]
    public void MakePathENVSafe_collapses_and_adds_one_trailing_separator()
    {
        var sep = OperatingSystem.IsWindows() ? "\\" : "/";
        var root = OperatingSystem.IsWindows() ? @"C:\data\\x" : "/data//x";
        var expected = (OperatingSystem.IsWindows() ? @"C:\data\x" : "/data/x") + sep;
        Assert.Equal(expected, FileUtility.MakePathENVSafe(root));
        Assert.Equal(expected, FileUtility.MakePathENVSafe(expected));
    }

    [Theory]
    [InlineData("photo.jpg", "photo", ".jpg")]
    [InlineData("archive.tar.gz", "archive.tar", ".gz")]
    [InlineData("noext", "noext", "")]
    public void File_name_and_extension_helpers(string path, string stem, string ext)
    {
        Assert.Equal(stem, FileUtility.FileNameMinusExtension(path));
        Assert.Equal(ext, FileUtility.FilenameExtension(path));
    }

    [Fact]
    public void JoinPanelPath_uses_the_platform_separator_for_local_paths()
    {
        var sep = OperatingSystem.IsWindows() ? "\\" : "/";
        Assert.Equal("base" + sep + "child", FileUtility.JoinPanelPath("base", "child"));
        Assert.Equal("base" + sep + "child", FileUtility.JoinPanelPath("base" + sep, "child"));
    }

    [Fact]
    public void JoinPanelPath_honours_uri_schemes()
    {
        Assert.Equal("cloud://gdrive/a/b", FileUtility.JoinPanelPath("cloud://gdrive/a", "b"));
        Assert.Equal("cloud://gdrive/b", FileUtility.JoinPanelPath("cloud://gdrive/", "b"));
        Assert.Equal("archive:///x.zip!/docs/a.md", FileUtility.JoinPanelPath("archive:///x.zip!/docs", "a.md"));
        Assert.Equal("archive:///x.zip!/a.md", FileUtility.JoinPanelPath("archive:///x.zip!/", "a.md"));
    }
}

public class AFileEntryTests
{
    [Theory]
    [InlineData(0L, "")]
    [InlineData(-5L, "")]
    [InlineData(1L, "1b")]
    [InlineData(1023L, "1023b")]
    [InlineData(1024L, "1Kb")]
    [InlineData(1536L, "1.5Kb")]
    [InlineData(1100L, "1.07Kb")]
    [InlineData(1024L * 1024, "1Mb")]
    [InlineData(3L * 1024 * 1024 * 1024, "3Gb")]
    [InlineData(2L * 1024 * 1024 * 1024 * 1024, "2Tb")]
    public void Size_is_formatted_in_binary_units(long size, string expected)
    {
        var e = Invariant.Run(() => new AFileEntry("f", size, false));
        Assert.Equal(expected, e.FileSize);
    }

    [Fact]
    public void Constructors_set_fields()
    {
        var e = new AFileEntry("dir", 0, true, 3, 0, "RW");
        Assert.True(e.Typ);
        Assert.Equal("dir", e.Name);
        Assert.Equal("3", e.Dirs);
        Assert.Equal("", e.Files); // zero counts are blank
        Assert.Equal("RW", e.Flags);

        var f = new AFileEntry("f.txt", 10, false, "RO", "2024-01-01");
        Assert.False(f.Typ);
        Assert.Equal("2024-01-01", f.FileDate);
        Assert.Equal("RO", f.Flags);
        Assert.Equal(RowCompareState.None, f.CompareState);
    }
}
