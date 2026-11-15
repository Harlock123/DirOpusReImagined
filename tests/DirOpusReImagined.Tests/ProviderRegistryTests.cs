using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined.Tests;

/// <summary>The archive and cloud providers are registered once by <see cref="TestSetup"/>, as MainWindow does.</summary>
public class ProviderRegistryTests
{
    [Theory]
    [InlineData("/home/user/file.txt")]
    [InlineData(@"C:\Users\me")]
    [InlineData(@"\\server\share")]
    [InlineData("relative/path")]
    [InlineData("")]
    public void Plain_paths_route_to_the_local_provider(string path)
    {
        Assert.IsType<LocalFileProvider>(ProviderRegistry.For(path));
        Assert.Same(ProviderRegistry.Local, ProviderRegistry.For(path));
    }

    [Theory]
    [InlineData("archive:///home/u/a.zip!/")]
    [InlineData("archive://C:\\data\\a.zip!/docs")]
    [InlineData("ARCHIVE:///x.zip")]
    public void Archive_uris_route_to_the_archive_provider(string path)
        => Assert.IsType<ArchiveFileProvider>(ProviderRegistry.For(path));

    [Theory]
    [InlineData("cloud://gdrive/")]
    [InlineData("cloud://gdrive/photos/2024")]
    [InlineData("Cloud://remote")]
    public void Cloud_uris_route_to_the_rclone_provider(string path)
    {
        var p = ProviderRegistry.For(path);
        Assert.IsType<RcloneFileProvider>(p);
        Assert.True(p.IsRemote);
    }

    [Fact]
    public void Local_provider_is_the_fallback_for_scheme_lookalikes()
    {
        Assert.IsType<LocalFileProvider>(ProviderRegistry.For("/tmp/cloud://x"));
        Assert.IsType<LocalFileProvider>(ProviderRegistry.For("archive:"));
    }
}
