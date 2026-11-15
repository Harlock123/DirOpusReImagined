namespace DirOpusReImagined.Tests;

public class BatchAttributesTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    private const UnixFileMode M644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private const UnixFileMode M755 = M644 | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

    private static string After(string spec, UnixFileMode current, bool isDir = false)
    {
        Assert.True(ModeChange.TryParse(spec, out var c, out var error), error);
        return ModeChange.Format(c.ApplyTo(current, isDir));
    }

    [Theory]
    [InlineData("755", "rw-r--r--", "rwxr-xr-x")]
    [InlineData("0600", "rwxrwxrwx", "rw-------")]
    [InlineData("u+x", "rw-r--r--", "rwxr--r--")]
    [InlineData("+x", "rw-r--r--", "rwxr-xr-x")]          // no "who" means everyone
    [InlineData("go-w", "rw-rw-rw-", "rw-r--r--")]
    [InlineData("a=r", "rwxrwxrwx", "r--r--r--")]
    [InlineData("o=", "rwxrwxrwx", "rwxrwx---")]
    [InlineData("u=rw,g=r,o=", "rwxrwxrwx", "rw-r-----")]
    [InlineData("ug+rw,o-rwx", "r--r--r--", "rw-rw----")]
    public void Chmod_notation_changes_the_mode_as_chmod_does(string spec, string before, string after)
        => Assert.Equal(after, After(spec, Parse(before)));

    [Fact]
    public void Capital_X_adds_execute_only_to_folders_and_already_executable_files()
    {
        Assert.Equal("rwxr-xr-x", After("a+X", M644, isDir: true));
        Assert.Equal("rw-r--r--", After("a+X", M644, isDir: false));
        Assert.Equal("rwxr-xr-x", After("a+X", M644 | UnixFileMode.UserExecute, isDir: false));
    }

    [Fact]
    public void Special_bits()
    {
        Assert.True(ModeChange.TryParse("u+s", out var c, out _));
        Assert.True(c.ApplyTo(M755, false).HasFlag(UnixFileMode.SetUser));
        Assert.True(ModeChange.TryParse("+t", out c, out _));
        Assert.True(c.ApplyTo(M755, true).HasFlag(UnixFileMode.StickyBit));
        Assert.True(ModeChange.TryParse("1777", out c, out _));
        Assert.True(c.ApplyTo(0, true).HasFlag(UnixFileMode.StickyBit));
        // Three octal digits leave the special bits alone.
        Assert.True(ModeChange.TryParse("755", out c, out _));
        Assert.True(c.ApplyTo(M755 | UnixFileMode.SetGroup, true).HasFlag(UnixFileMode.SetGroup));
    }

    [Theory]
    [InlineData("")]
    [InlineData("75")]
    [InlineData("77777")]
    [InlineData("u")]
    [InlineData("u*x")]
    [InlineData("u+q")]
    [InlineData("z+x")]
    public void Nonsense_is_refused_with_a_reason(string spec)
    {
        Assert.False(ModeChange.TryParse(spec, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    private static UnixFileMode Parse(string rwx)
    {
        UnixFileMode m = 0;
        UnixFileMode[] bits =
        {
            UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute,
            UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute,
            UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute,
        };
        for (int i = 0; i < 9; i++) if (rwx[i] != '-') m |= bits[i];
        return m;
    }

    [Theory]
    [InlineData("2026-11-15 14:30:05", 2026, 11, 15, 14, 30, 5)]
    [InlineData("2026-11-15 14:30", 2026, 11, 15, 14, 30, 0)]
    [InlineData("2026-11-15", 2026, 11, 15, 0, 0, 0)]
    public void Dates_are_read(string text, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(BatchAttributes.TryParseDate(text, out var when));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), when);
    }

    [Fact]
    public void A_bad_date_is_refused() => Assert.False(BatchAttributes.TryParseDate("next tuesday-ish", out _));

    [Fact]
    public void Folder_contents_are_included_only_when_asked_and_links_not_followed()
    {
        _t.File("top/a.txt", "a");
        _t.File("top/sub/b.txt", "b");
        var sel = new[] { (_t.Combine("top"), true) };
        Assert.Single(BatchAttributes.Targets(sel, includeContents: false));

        try { Directory.CreateSymbolicLink(_t.Combine("top/loop"), _t.Combine("top")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var all = BatchAttributes.Targets(sel, includeContents: true).Select(t => Path.GetFileName(t.Path)).OrderBy(n => n).ToList();
        Assert.Contains("a.txt", all);
        Assert.Contains("b.txt", all);
        Assert.Contains("sub", all);
        Assert.True(all.Count <= 6);   // the link itself may appear, never what's behind it again
    }

    [Fact]
    public void Dates_and_modes_are_applied_to_real_files()
    {
        var file = _t.File("x/f.txt", "f");
        var dir = _t.Dir("x/d");
        var when = new DateTime(2020, 5, 6, 7, 8, 9);
        ModeChange? mode = null;
        if (!OperatingSystem.IsWindows()) ModeChange.TryParse("u=rwX,go=", out mode, out _);

        var problems = BatchAttributes.Apply(new[] { (file, false), (dir, true) }, new AttributeChanges(when, mode, null, false));

        Assert.Empty(problems);
        Assert.Equal(when, File.GetLastWriteTime(file));
        Assert.Equal(when, Directory.GetLastWriteTime(dir));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("rw-------", ModeChange.Format(File.GetUnixFileMode(file)));
            Assert.Equal("rwx------", ModeChange.Format(File.GetUnixFileMode(dir)));
        }
    }

    [Fact]
    public void A_missing_item_is_reported_and_the_rest_still_change()
    {
        var file = _t.File("y/ok.txt", "ok");
        var when = new DateTime(2021, 1, 2, 3, 4, 5);
        var problems = BatchAttributes.Apply(new[] { (_t.Combine("y/gone.txt"), false), (file, false) },
                                             new AttributeChanges(when, null, null, false));
        Assert.Single(problems);
        Assert.Equal(when, File.GetLastWriteTime(file));
    }
}
