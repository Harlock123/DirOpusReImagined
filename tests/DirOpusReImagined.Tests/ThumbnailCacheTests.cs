using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined.Tests;

public class ThumbnailCacheTests
{
    [Fact]
    public void The_same_file_and_size_always_key_the_same()
        => Assert.Equal(ThumbnailCache.KeyFor("/a/b.jpg", 10, 20, 128), ThumbnailCache.KeyFor(@"\a\b.jpg", 10, 20, 128));

    [Fact]
    public void Any_change_gives_a_new_key()
    {
        string k = ThumbnailCache.KeyFor("/a/b.jpg", 10, 20, 128);
        Assert.NotEqual(k, ThumbnailCache.KeyFor("/a/c.jpg", 10, 20, 128));
        Assert.NotEqual(k, ThumbnailCache.KeyFor("/a/b.jpg", 11, 20, 128));
        Assert.NotEqual(k, ThumbnailCache.KeyFor("/a/b.jpg", 10, 21, 128));
        Assert.NotEqual(k, ThumbnailCache.KeyFor("/a/b.jpg", 10, 20, 96));
    }

    [Fact]
    public void Thumbnails_made_before_photos_were_turned_upright_are_not_reused()
    {
        // The key as it was before the cache version was added: those cached thumbnails are sideways.
        string payload = "/a/b.jpg|10|20|128";
        string oldKey = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        Assert.True(ThumbnailCache.CacheVersion >= 2);
        Assert.NotEqual(oldKey, ThumbnailCache.KeyFor("/a/b.jpg", 10, 20, 128));
    }
}
