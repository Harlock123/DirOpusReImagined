using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem;
//using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirOpusReImagined
{
    /// <summary>
    /// Provides utility methods for file operations.
    /// </summary>
    public static class FileUtility
    {
        /// <summary>
        /// Fired on the UI thread after a panel's items have been replaced with
        /// a freshly-enumerated list. Subscribers typically refresh dependent UI
        /// (status bar counts, breadcrumbs) that read from the grid's Items.
        /// </summary>
        public static event Action<TaiDataGrid>? PanelPopulated;

        private static Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return desktop.MainWindow;
            return null;
        }

        public static long GetDirectorySize(string path)
            => ProviderRegistry.For(path).GetDirectorySize(path, recursive: false);

        public static long GetDirectorySizeRecursive(string path)
            => ProviderRegistry.For(path).GetDirectorySize(path, recursive: true);

        public static void CopyFileToFolder(string sourceFile, string targetFolder)
        {
            try
            {
                var dstP = ProviderRegistry.For(targetFolder);
                dstP.CreateDirectory(targetFolder);

                string targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));

                CopyFileAcrossProviders(sourceFile, targetFile, overwrite: true);

                Console.WriteLine($"File {sourceFile} was copied to {targetFolder}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine("Error copying file: " + ioEx.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }

        private static void CopyFileAcrossProviders(string src, string dst, bool overwrite)
        {
            var srcP = ProviderRegistry.For(src);
            var dstP = ProviderRegistry.For(dst);

            if (ReferenceEquals(srcP, dstP))
            {
                srcP.CopyFile(src, dst, overwrite);
                return;
            }

            if (!overwrite && dstP.FileExists(dst))
                throw new IOException($"Destination exists: {dst}");

            using var inStream = srcP.OpenRead(src);
            using var outStream = dstP.OpenWrite(dst);
            inStream.CopyTo(outStream);
        }

        // ---- Async, progress-reporting, cancellable transfer variants ------------------------
        //
        // These mirror the synchronous methods above (same provider branching) but thread an
        // IProgress<TransferProgress> and CancellationToken through, and use the provider's
        // CopyFileAsync for the byte-moving leg so remote (rclone) copies report real progress.

        /// <summary>Copies a single file into a folder, reporting byte-level progress.</summary>
        public static async Task CopyFileToFolderAsync(string sourceFile, string targetFolder,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            var dstP = ProviderRegistry.For(targetFolder);
            dstP.CreateDirectory(targetFolder);

            string targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));
            await CopyFileAcrossProvidersAsync(sourceFile, targetFile, overwrite: true, progress, ct)
                .ConfigureAwait(false);
        }

        /// <summary>Recursively copies a directory, reporting cumulative byte progress for the tree.</summary>
        public static async Task CopyDirectoryToFolderAsync(string sourceDirectory, string targetDirectory,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            var counter = new ByteCounter();
            await CopyDirectoryInternalAsync(sourceDirectory, targetDirectory, progress, counter, ct)
                .ConfigureAwait(false);
        }

        private static async Task CopyDirectoryInternalAsync(string sourceDirectory, string targetDirectory,
            IProgress<TransferProgress>? progress, ByteCounter counter, CancellationToken ct)
        {
            var srcP = ProviderRegistry.For(sourceDirectory);
            var dstP = ProviderRegistry.For(targetDirectory);
            dstP.CreateDirectory(targetDirectory);

            foreach (var fileEntry in srcP.EnumerateFiles(sourceDirectory))
            {
                ct.ThrowIfCancellationRequested();
                string targetFile = Path.Combine(targetDirectory, fileEntry.Name);

                // Offset this file's bytes by what the tree has already transferred.
                long baseDone = counter.Total;
                var fileProgress = new SyncProgress<TransferProgress>(tp =>
                    progress?.Report(new TransferProgress(
                        string.IsNullOrEmpty(tp.CurrentFile) ? fileEntry.Name : tp.CurrentFile,
                        0, 0, baseDone + tp.BytesDone, 0, tp.BytesPerSecond)));

                await CopyFileAcrossProvidersAsync(fileEntry.Path, targetFile, overwrite: true, fileProgress, ct)
                    .ConfigureAwait(false);

                counter.Total += fileEntry.Size;
                progress?.Report(new TransferProgress(fileEntry.Name, 0, 0, counter.Total, 0, 0));
            }

            foreach (var dirEntry in srcP.EnumerateDirectories(sourceDirectory))
            {
                ct.ThrowIfCancellationRequested();
                string targetDir = Path.Combine(targetDirectory, dirEntry.Name);
                await CopyDirectoryInternalAsync(dirEntry.Path, targetDir, progress, counter, ct)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>Moves a single file into a folder; server-side rename when possible, else copy+delete.</summary>
        public static async Task MoveFileAsync(string sourceFile, string targetDirectory,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            var srcP = ProviderRegistry.For(sourceFile);
            var dstP = ProviderRegistry.For(targetDirectory);

            dstP.CreateDirectory(targetDirectory);
            string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));

            // Moving a file onto itself (same folder) is a no-op — never touch the only copy.
            if (ReferenceEquals(srcP, dstP) &&
                string.Equals(sourceFile, targetFile, StringComparison.OrdinalIgnoreCase))
                return;

            if (ReferenceEquals(srcP, dstP))
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    // The caller confirms overwrite before we get here; clear any existing target so
                    // the move doesn't fail with "already exists" (File.Move won't overwrite).
                    if (dstP.FileExists(targetFile)) dstP.DeleteFile(targetFile);
                    srcP.MoveFile(sourceFile, targetFile);
                }, ct).ConfigureAwait(false);
            }
            else
            {
                await CopyFileAcrossProvidersAsync(sourceFile, targetFile, overwrite: true, progress, ct)
                    .ConfigureAwait(false);
                srcP.DeleteFile(sourceFile);
            }
        }

        /// <summary>Moves a directory; server-side move when possible, else recursive copy+delete.</summary>
        public static async Task MoveDirectoryAsync(string sourceDirectory, string targetDirectory,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            var srcP = ProviderRegistry.For(sourceDirectory);
            var dstP = ProviderRegistry.For(targetDirectory);

            // Moving a folder onto itself is a no-op — never merge-and-delete the only copy.
            if (ReferenceEquals(srcP, dstP) &&
                string.Equals(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase))
                return;

            // A same-provider rename is only valid when the destination doesn't already exist —
            // Directory.Move can't merge into an existing folder. When it does exist (overwrite already
            // confirmed), merge the tree in file-by-file and then remove the source, same as the
            // cross-provider path below.
            if (ReferenceEquals(srcP, dstP) && !dstP.DirectoryExists(targetDirectory))
            {
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    srcP.MoveDirectory(sourceDirectory, targetDirectory);
                }, ct).ConfigureAwait(false);
            }
            else
            {
                await CopyDirectoryToFolderAsync(sourceDirectory, targetDirectory, progress, ct)
                    .ConfigureAwait(false);
                srcP.DeleteDirectory(sourceDirectory, recursive: true);
            }
        }

        private static async Task CopyFileAcrossProvidersAsync(string src, string dst, bool overwrite,
            IProgress<TransferProgress>? progress, CancellationToken ct)
        {
            var srcP = ProviderRegistry.For(src);
            var dstP = ProviderRegistry.For(dst);

            if (ReferenceEquals(srcP, dstP))
            {
                // Same provider: local→local falls back to File.Copy; cloud→cloud drives a
                // server-side rclone job with real progress (RcloneFileProvider.CopyFileAsync).
                await srcP.CopyFileAsync(src, dst, overwrite, progress, ct).ConfigureAwait(false);
                return;
            }

            if (!overwrite && dstP.FileExists(dst))
                throw new IOException($"Destination exists: {dst}");

            // Cross-provider is always local<->cloud (only one remote provider exists). Let the
            // remote provider own the rclone leg so the slow network transfer reports real progress,
            // rather than streaming through OpenRead/OpenWrite's temp-file round-trip.
            if (srcP.IsRemote && !dstP.IsRemote)
            {
                await srcP.CopyToLocalAsync(src, dst, progress, ct).ConfigureAwait(false);
            }
            else if (!srcP.IsRemote && dstP.IsRemote)
            {
                await dstP.CopyFromLocalAsync(src, dst, overwrite, progress, ct).ConfigureAwait(false);
            }
            else
            {
                // Two non-remote providers (e.g. extracting from a read-only archive to local disk):
                // stream the bytes through OpenRead/OpenWrite.
                await Task.Run(() =>
                {
                    ct.ThrowIfCancellationRequested();
                    using var inStream = srcP.OpenRead(src);
                    using var outStream = dstP.OpenWrite(dst);
                    inStream.CopyTo(outStream);
                }, ct).ConfigureAwait(false);
            }
        }

        private sealed class ByteCounter { public long Total; }

        /// <summary>
        /// Copies the contents of a directory to another directory, including subdirectories.
        /// </summary>
        /// <param name="sourceDirectory">The path of the source directory to be copied.</param>
        /// <param name="targetDirectory">The path of the target directory where the contents will be copied.</param>
        public static void CopyDirectoryToFolder(string sourceDirectory, string targetDirectory)
        {
            try
            {
                var srcP = ProviderRegistry.For(sourceDirectory);
                var dstP = ProviderRegistry.For(targetDirectory);

                dstP.CreateDirectory(targetDirectory);

                foreach (var fileEntry in srcP.EnumerateFiles(sourceDirectory))
                {
                    string targetFile = Path.Combine(targetDirectory, fileEntry.Name);
                    CopyFileAcrossProviders(fileEntry.Path, targetFile, overwrite: true);
                }

                foreach (var dirEntry in srcP.EnumerateDirectories(sourceDirectory))
                {
                    string targetDir = Path.Combine(targetDirectory, dirEntry.Name);
                    CopyDirectoryToFolder(dirEntry.Path, targetDir);
                }

                Console.WriteLine($"Directory {sourceDirectory} was copied to {targetDirectory}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine("Error copying directory: " + ioEx.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }

        /// <summary>
        /// Moves a file from the source location to the target directory.
        /// If the target directory doesn't exist, it will be created.
        /// </summary>
        /// <param name="sourceFile">The full path of the file to be moved.</param>
        /// <param name="targetDirectory">The target directory where the file will be moved to.</param>
        public static void MoveFile(string sourceFile, string targetDirectory)
        {
            try
            {
                var srcP = ProviderRegistry.For(sourceFile);
                var dstP = ProviderRegistry.For(targetDirectory);

                dstP.CreateDirectory(targetDirectory);

                string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));

                if (ReferenceEquals(srcP, dstP))
                {
                    srcP.MoveFile(sourceFile, targetFile);
                }
                else
                {
                    CopyFileAcrossProviders(sourceFile, targetFile, overwrite: false);
                    srcP.DeleteFile(sourceFile);
                }

                Console.WriteLine($"File {sourceFile} was moved to {targetDirectory}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine("Error moving file: " + ioEx.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }

        /// Moves a directory from the source directory to the target directory.
        /// </summary>
        /// <param name="sourceDirectory">The path of the directory to be moved.</param>
        /// <param name="targetDirectory">The path of the target directory where the source directory will be moved to.</param>
        /// <exception cref="IOException">Thrown if an I/O error occurs while moving the directory.</exception>
        /// <exception cref="Exception">Thrown if an unexpected error occurs.</exception>
        public static void MoveDirectory(string sourceDirectory, string targetDirectory)
        {
            try
            {
                var srcP = ProviderRegistry.For(sourceDirectory);
                var dstP = ProviderRegistry.For(targetDirectory);

                if (ReferenceEquals(srcP, dstP))
                {
                    srcP.MoveDirectory(sourceDirectory, targetDirectory);
                }
                else
                {
                    CopyDirectoryToFolder(sourceDirectory, targetDirectory);
                    srcP.DeleteDirectory(sourceDirectory, recursive: true);
                }

                Console.WriteLine($"Directory {sourceDirectory} was moved to {targetDirectory}");
            }
            catch (IOException ioEx)
            {
                Console.WriteLine("Error moving directory: " + ioEx.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unexpected error: " + ex.Message);
            }
        }

        /// <summary>
        /// Renames a file from the specified old file path to the specified new file path.
        /// </summary>
        /// <param name="oldFilePath">The path of the file to be renamed.</param>
        /// <param name="newFilePath">The new path for the file after renaming.</param>
        /// <summary>Renames (moves) a file. Returns true only if the rename actually happened;
        /// surfaces collisions/errors to the user rather than swallowing them.</summary>
        public static bool RenameFile(string oldFilePath, string newFilePath)
        {
            try
            {
                var p = ProviderRegistry.For(oldFilePath);

                if (!p.FileExists(oldFilePath))
                {
                    new MessageBox($"Cannot rename — the file no longer exists:\n{oldFilePath}", "Rename Failed")
                        .ShowDialog(GetMainWindow());
                    return false;
                }
                if (p.FileExists(newFilePath))
                {
                    new MessageBox($"A file named \"{Path.GetFileName(newFilePath)}\" already exists — rename skipped.",
                        "Rename Failed").ShowDialog(GetMainWindow());
                    return false;
                }

                p.MoveFile(oldFilePath, newFilePath);
                return true;
            }
            catch (Exception ex)
            {
                new MessageBox($"Error renaming the file: {ex.Message}", "Rename Failed").ShowDialog(GetMainWindow());
                return false;
            }
        }

        /// <summary>
        /// Renames a directory from the specified old directory path to the specified new directory path.
        /// </summary>
        /// <param name="olddir">The path of the directory to be renamed.</param>
        /// <param name="newdir">The new path for the renamed directory.</param>
        /// <summary>Renames (moves) a directory. Returns true only if it actually happened.</summary>
        public static bool RenameDirectory(string olddir, string newdir)
        {
            try
            {
                var p = ProviderRegistry.For(olddir);

                if (!p.DirectoryExists(olddir) || p.DirectoryExists(newdir))
                {
                    new MessageBox("The source directory does not exist or the target directory already exists.",
                        "Rename Failed").ShowDialog(GetMainWindow());
                    return false;   // don't fall through to MoveDirectory on a collision
                }

                p.MoveDirectory(olddir, newdir);
                return true;
            }
            catch (Exception ex)
            {
                new MessageBox($"Error renaming the directory: {ex.Message}", "Rename Failed").ShowDialog(GetMainWindow());
                return false;
            }
        }

        /// <summary>
        /// Collapses runs of repeated separators in a path while preserving a leading UNC prefix
        /// (<c>\\SERVER\SHARE</c> or <c>//SERVER/SHARE</c>).
        /// </summary>
        /// <remarks>
        /// A UNC path's leading double separator IS the prefix. Collapsing it turns
        /// <c>\\SERVER\SHARE\FOLDER</c> into <c>\SERVER\SHARE\FOLDER</c>, which Windows resolves
        /// against the process's current drive — so a copy to a network share silently lands in
        /// <c>C:\SERVER\SHARE\FOLDER</c> instead, creating that tree on the way. Keep the first two
        /// characters when they're both separators and only de-duplicate what follows.
        /// </remarks>
        public static string CollapseSeparators(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            int start = IsSeparator(path[0]) && path.Length >= 2 && IsSeparator(path[1]) ? 2 : 0;

            var sb = new System.Text.StringBuilder(path.Length);
            sb.Append(path, 0, start);
            for (int i = start; i < path.Length; i++)
            {
                if (IsSeparator(path[i]) && sb.Length > 0 && IsSeparator(sb[sb.Length - 1])) continue;
                sb.Append(path[i]);
            }
            return sb.ToString();
        }

        private static bool IsSeparator(char c) => c == '\\' || c == '/';

        /// <summary>
        /// Collapses repeated separators in the given path (UNC prefix preserved).
        /// Appends a trailing backslash for Windows OS or a trailing forward slash for Unix/MacOSX OS if necessary.
        /// </summary>
        /// <param name="path">The original path.</param>
        /// <returns>The modified path.</returns>
        public static string MakePathENVSafe(string path)
        {
            string result = CollapseSeparators(path);

            // now for some environmental stuff
            
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                if (!result.EndsWith(@"\"))
                {
                    result += @"\";

                }
            }
            else if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                if (!result.EndsWith(@"/"))
                {
                    result += @"/";

                }
                
            }

            return result;

        }

        /// <summary>
        /// Returns the file name without the extension from the specified path.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>The file name without the extension.</returns>
        public static string FileNameMinusExtension(string path)
        {
            string result = Path.GetFileNameWithoutExtension(path);

            return result;
        }

        /// <summary>
        /// Gets the extension of a file name from the provided path.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>The extension of the file name.</returns>
        public static string FilenameExtension(string path)
        {
            string result = Path.GetExtension(path);

            return result;
        }

        /// <summary>
        /// Populates a file panel with the directories and files from a specified path.
        /// </summary>
        /// <param name="ThePanel">The file panel to populate.</param>
        /// <param name="PATHNAME">The path name of the directory to populate from.</param>
        /// <param name="ShowHidden">A boolean value indicating whether to show hidden files.</param>
        /// <param name="SortByName">A boolean value indicating the results get sorted by Name</param>
        public static void PopulateFilePanel(TaiDataGrid ThePanel, string PATHNAME, bool ShowHidden, SortSpec Sort)
        {
            //LPgrid.PopulateGrid(PATHNAME);

            //var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME);

            // using linq to sort the directories by name alphabetically

            if (PATHNAME == null || PATHNAME == "")
            {
                return;
            }


            var provider = ProviderRegistry.For(PATHNAME);

            if (provider.IsRemote)
            {
                PopulateFilePanelAsync(ThePanel, PATHNAME, ShowHidden, Sort, provider);
                return;
            }

            try
            {
                ThePanel.SuspendRendering = true;
                ThePanel.Items.Clear();
                ThePanel.Items = BuildFileList(provider, PATHNAME, ShowHidden, Sort).OfType<object>().ToList();
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message, "Error");
                MB.ShowDialog(GetMainWindow());
            }

            ThePanel.SuspendRendering = false;
            PanelPopulated?.Invoke(ThePanel);
        }

        private static void PopulateFilePanelAsync(
            TaiDataGrid ThePanel, string PATHNAME, bool ShowHidden, SortSpec Sort, IFileProvider provider)
        {
            // A cloud remote's first listing after launch can take ~20s while rclone spins up the
            // backend and refreshes its OAuth token; say so, so the panel doesn't look frozen.
            var loadingText = provider.IsRemote
                ? "Loading… (first cloud access can take ~20s)"
                : "Loading…";
            ThePanel.SuspendRendering = true;
            ThePanel.Items = new List<object>
            {
                new AFileEntry(loadingText, 0, true, 0, 0, ""),
            };
            ThePanel.SuspendRendering = false;

            Task.Run(() =>
            {
                List<AFileEntry>? list = null;
                Exception? error = null;
                try { list = BuildFileList(provider, PATHNAME, ShowHidden, Sort); }
                catch (Exception ex) { error = ex; }

                Dispatcher.UIThread.Post(() =>
                {
                    ThePanel.SuspendRendering = true;
                    if (error is not null)
                    {
                        ThePanel.Items = new List<object>();
                        new MessageBox(error.Message, "Error").ShowDialog(GetMainWindow());
                    }
                    else
                    {
                        ThePanel.Items = list!.OfType<object>().ToList();
                    }
                    ThePanel.SuspendRendering = false;
                    PanelPopulated?.Invoke(ThePanel);
                });
            });
        }

        private static List<AFileEntry> BuildFileList(IFileProvider provider, string PATHNAME, bool ShowHidden, SortSpec Sort)
            => BuildFileListCore(provider, PATHNAME, ShowHidden, Sort);

        private static List<AFileEntry> BuildFileListCore(IFileProvider provider, string PATHNAME, bool ShowHidden)
            => BuildFileListCore(provider, PATHNAME, ShowHidden, SortSpec.Default);

        private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;

        private static string Extension(string name)
        {
            int dot = name.LastIndexOf('.');
            return dot > 0 ? name.Substring(dot + 1).ToLowerInvariant() : "";
        }

        // Sorts files on the RAW FileEntry fields (typed Size/LastModified/Name) so numbers and dates
        // order correctly — AFileEntry stores display strings, which would sort as text.
        private static IEnumerable<FileEntry> SortFiles(IEnumerable<FileEntry> files, SortSpec sort) => sort.Key switch
        {
            SortKey.Size => sort.Ascending ? files.OrderBy(e => e.Size) : files.OrderByDescending(e => e.Size),
            SortKey.Date => sort.Ascending ? files.OrderBy(e => e.LastModified) : files.OrderByDescending(e => e.LastModified),
            SortKey.Type => sort.Ascending
                ? files.OrderBy(e => Extension(e.Name)).ThenBy(e => e.Name, NameComparer)
                : files.OrderByDescending(e => Extension(e.Name)).ThenBy(e => e.Name, NameComparer),
            _            => sort.Ascending ? files.OrderBy(e => e.Name, NameComparer) : files.OrderByDescending(e => e.Name, NameComparer),
        };

        // Folders always come first, sorted alphabetically; files follow, ordered by the panel's
        // SortSpec (name / size / date / type, ascending or descending).
        private static List<AFileEntry> BuildFileListCore(IFileProvider provider, string PATHNAME, bool ShowHidden, SortSpec sort)
        {
            var directoryEntries = provider.EnumerateDirectories(PATHNAME)
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var fileEntries = SortFiles(provider.EnumerateFiles(PATHNAME), sort).ToList();

            var result = new List<AFileEntry>();

            foreach (var entry in directoryEntries)
            {
                try
                {
                    if (entry.Flags.HasFlag(FileEntryFlags.System)) continue;
                    if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;

                    int ds = 0, fs = 0;
                    long dirSize = 0;
                    if (!provider.IsRemote)
                    {
                        try
                        {
                            ds = provider.EnumerateDirectories(entry.Path).Count();
                            fs = provider.EnumerateFiles(entry.Path).Count();
                            dirSize = provider.GetDirectorySize(entry.Path, recursive: false);
                        }
                        catch { }
                    }

                    result.Add(new AFileEntry(entry.Name, dirSize, true, ds, fs, entry.AttributeString));
                }
                catch
                {
                    try { result.Add(new AFileEntry(entry.Name, 0, true, 0, 0, "")); }
                    catch { }
                }
            }

            foreach (var entry in fileEntries)
            {
                try
                {
                    if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;
                    string ft = entry.LastModified.ToShortDateString() + " " + entry.LastModified.ToShortTimeString();
                    result.Add(new AFileEntry(entry.Name, entry.Size, false, entry.AttributeString, ft));
                }
                catch { }
            }

            return result;
        }

/// <summary>
        /// Populates a file panel with the directories and files from a specified path.
        /// </summary>
        /// <param name="ThePanel">The file panel to populate.</param>
        /// <param name="PATHNAME">The path name of the directory to populate from.</param>
        /// <param name="ShowHidden">A boolean value indicating whether to show hidden files.</param>
        public static void PopulateFilePanel(TaiDataGrid ThePanel, string PATHNAME, bool ShowHidden)
        {
            //LPgrid.PopulateGrid(PATHNAME);

            //var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME);

            // using linq to sort the directories by name alphabetically
            
            if (PATHNAME == null || PATHNAME == "")
            {
                return;
            }
            
            
            var provider = ProviderRegistry.For(PATHNAME);

            if (provider.IsRemote)
            {
                PopulateFilePanelAsync2(ThePanel, PATHNAME, ShowHidden, provider);
                return;
            }

            try
            {
                ThePanel.SuspendRendering = true;
                ThePanel.Items.Clear();
                ThePanel.Items = BuildFileListCore(provider, PATHNAME, ShowHidden).OfType<object>().ToList();
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message, "Error");
                MB.ShowDialog(GetMainWindow());
            }

            ThePanel.SuspendRendering = false;
            PanelPopulated?.Invoke(ThePanel);
        }

        private static void PopulateFilePanelAsync2(
            TaiDataGrid ThePanel, string PATHNAME, bool ShowHidden, IFileProvider provider)
        {
            ThePanel.SuspendRendering = true;
            ThePanel.Items = new List<object>
            {
                new AFileEntry("Loading…", 0, true, 0, 0, ""),
            };
            ThePanel.SuspendRendering = false;

            Task.Run(() =>
            {
                List<AFileEntry>? list = null;
                Exception? error = null;
                try { list = BuildFileListCore(provider, PATHNAME, ShowHidden); }
                catch (Exception ex) { error = ex; }

                Dispatcher.UIThread.Post(() =>
                {
                    ThePanel.SuspendRendering = true;
                    if (error is not null)
                    {
                        ThePanel.Items = new List<object>();
                        new MessageBox(error.Message, "Error").ShowDialog(GetMainWindow());
                    }
                    else
                    {
                        ThePanel.Items = list!.OfType<object>().ToList();
                    }
                    ThePanel.SuspendRendering = false;
                    PanelPopulated?.Invoke(ThePanel);
                });
            });
        }

        /// <summary>
        /// Deletes a folder and its contents.
        /// </summary>
        /// <param name="rootPath">The root path of the folder to delete.</param>
        public static void DeleteFolder(string rootPath) => DeleteFolder(rootPath, useTrash: false);

        public static void DeleteFolder(string rootPath, bool useTrash)
        {
            try
            {
                var p = ProviderRegistry.For(rootPath);
                if (p.DirectoryExists(rootPath))
                {
                    if (CanTrash(p, rootPath, useTrash))
                        TrashService.Trash(rootPath);
                    else
                        p.DeleteDirectory(rootPath, recursive: true);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message, "Error");
                MB.ShowDialog(GetMainWindow());
            }
        }

        /// <summary>
        /// This method deletes a file at the specified root path.
        /// </summary>
        /// <param name="rootPath">The root path of the file to be deleted.</param>
        public static void DeleteFile(string rootPath) => DeleteFile(rootPath, useTrash: false);

        public static void DeleteFile(string rootPath, bool useTrash)
        {
            try
            {
                var p = ProviderRegistry.For(rootPath);
                if (p.FileExists(rootPath))
                {
                    if (CanTrash(p, rootPath, useTrash))
                        TrashService.Trash(rootPath);
                    else
                        p.DeleteFile(rootPath);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message, "Error");
                MB.ShowDialog(GetMainWindow());
            }
        }

        // Trash applies only to local files — cloud has no recycle bin and archives are read-only.
        private static bool CanTrash(IFileProvider provider, string path, bool useTrash)
            => useTrash && !provider.IsRemote
               && !DirOpusReImagined.FileSystem.Archive.ArchivePath.IsArchiveUri(path);
    }
}
