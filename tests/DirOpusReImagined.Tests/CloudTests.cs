using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined.Tests;

public class CloudPathTests
{
    [Theory]
    [InlineData("cloud://gdrive", "gdrive", "")]
    [InlineData("cloud://gdrive/", "gdrive", "")]
    [InlineData("cloud://gdrive/photos/2024", "gdrive", "photos/2024")]
    [InlineData("cloud://gdrive/photos/2024/", "gdrive", "photos/2024")]
    [InlineData("CLOUD://My Drive/a b/c", "My Drive", "a b/c")]
    public void Parse(string uri, string remote, string path)
    {
        var cp = CloudPath.Parse(uri);
        Assert.Equal(remote, cp.Remote);
        Assert.Equal(path, cp.Path);
    }

    [Fact]
    public void Parse_rejects_non_cloud_uris()
        => Assert.Throws<ArgumentException>(() => CloudPath.Parse("/home/u"));

    [Theory]
    [InlineData("cloud://x", true)]
    [InlineData("Cloud://x", true)]
    [InlineData("archive://x", false)]
    [InlineData("/cloud://x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsCloudUri(string? uri, bool expected) => Assert.Equal(expected, CloudPath.IsCloudUri(uri!));

    [Fact]
    public void FullUri_and_Fs()
    {
        Assert.Equal("cloud://gdrive/", new CloudPath("gdrive", "").FullUri);
        Assert.Equal("cloud://gdrive/a/b", new CloudPath("gdrive", "a/b").FullUri);
        Assert.Equal("gdrive:", new CloudPath("gdrive", "a").Fs);
        Assert.Equal("cloud://gdrive/a/b", CloudPath.Parse("cloud://gdrive/a/b/").FullUri);
    }

    [Fact]
    public void Join_and_WithPath()
    {
        var root = new CloudPath("r", "");
        Assert.Equal("a", root.Join("/a").Path);
        Assert.Equal("a/b", root.Join("a").Join("b").Path);
        Assert.Equal("a/b", new CloudPath("r", "a/").Join("b").Path);
        Assert.Equal(new CloudPath("r", "z"), root.WithPath("z"));
    }
}

/// <summary>RcloneListCache is process-wide static state; tests in this class run sequentially and
/// use unique keys so they don't disturb each other.</summary>
public class RcloneListCacheTests
{
    private static string Key() => "cloud://test-" + Guid.NewGuid().ToString("N") + "/";

    private static List<FileEntry> Rows(string name)
        => new() { new FileEntry("cloud://r/" + name, name, false, 1, Times.Base, FileEntryFlags.None) };

    [Fact]
    public void Miss_then_hit_after_set()
    {
        var key = Key();
        Assert.False(RcloneListCache.TryGet(key, out _));
        var rows = Rows("a");
        RcloneListCache.Set(key, rows);
        Assert.True(RcloneListCache.TryGet(key, out var got));
        Assert.Same(rows, got);
    }

    [Fact]
    public void Set_replaces_the_previous_rows()
    {
        var key = Key();
        RcloneListCache.Set(key, Rows("a"));
        var newer = Rows("b");
        RcloneListCache.Set(key, newer);
        Assert.True(RcloneListCache.TryGet(key, out var got));
        Assert.Same(newer, got);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        var k1 = Key();
        var k2 = Key();
        RcloneListCache.Set(k1, Rows("a"));
        RcloneListCache.Set(k2, Rows("b"));
        RcloneListCache.Clear();
        Assert.False(RcloneListCache.TryGet(k1, out _));
        Assert.False(RcloneListCache.TryGet(k2, out _));
    }

    [Fact]
    public void Keys_are_case_sensitive()
    {
        var key = Key();
        RcloneListCache.Set(key, Rows("a"));
        Assert.False(RcloneListCache.TryGet(key.ToUpperInvariant(), out _));
    }
}
