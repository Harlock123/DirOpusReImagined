using System.IO.Compression;
using DirOpusReImagined.FileSystem.Archive;

namespace DirOpusReImagined.Tests;

/// <summary>
/// The app uses one archive provider from many threads at once (browsing, previews, search,
/// transfers, archive creation), so its entry cache must cope with concurrent use.
/// </summary>
public class ArchiveConcurrencyTests : IDisposable
{
    private readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Many_threads_listing_many_archives_all_get_correct_results()
    {
        const int archives = 40;
        var paths = new string[archives];
        for (int i = 0; i < archives; i++)
        {
            paths[i] = _t.Combine($"a{i}.zip");
            using var z = ZipFile.Open(paths[i], ZipArchiveMode.Create);
            for (int f = 0; f <= i % 5; f++)
            {
                using var w = new StreamWriter(z.CreateEntry($"dir/file{f}.txt").Open());
                w.Write(f);
            }
        }

        var provider = new ArchiveFileProvider();
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 4000, new ParallelOptions { MaxDegreeOfParallelism = 16 }, n =>
        {
            int i = n % archives;
            try
            {
                int count = provider.EnumerateFiles(new ArchivePath(paths[i], "dir").FullUri).Count();
                if (count != i % 5 + 1) errors.Add($"a{i}.zip listed {count} files");
            }
            catch (Exception ex)
            {
                errors.Add($"a{i}.zip: {ex.GetType().Name}: {ex.Message}");
            }
        });

        Assert.Empty(errors);
    }
}
