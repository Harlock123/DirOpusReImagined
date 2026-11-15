using System.Runtime.CompilerServices;
using DirOpusReImagined.FileSystem;
using DirOpusReImagined.FileSystem.Archive;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined.Tests;

internal static class TestSetup
{
    /// <summary>
    /// Registers the archive and cloud providers once, before any test runs, the same way MainWindow
    /// does at start-up. ProviderRegistry is a static, unsynchronised list, so registering from tests
    /// running in parallel could race with lookups; a module initializer avoids that.
    /// </summary>
    [ModuleInitializer]
    internal static void RegisterProviders()
    {
        ProviderRegistry.Register(new RcloneFileProvider());
        ProviderRegistry.Register(new ArchiveFileProvider());
    }
}

/// <summary>Runs a block under the invariant culture so number formatting is predictable.</summary>
internal static class Invariant
{
    public static T Run<T>(Func<T> f)
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            return f();
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = saved; }
    }
}
