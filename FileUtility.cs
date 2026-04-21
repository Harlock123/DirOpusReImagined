using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using DirOpusReImagined.FileSystem;
//using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirOpusReImagined
{
    /// <summary>
    /// Provides utility methods for file operations.
    /// </summary>
    public static class FileUtility
    {
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
        public static void RenameFile(string oldFilePath, string newFilePath)
        {
            try
            {
                var p = ProviderRegistry.For(oldFilePath);

                if (!p.FileExists(oldFilePath))
                {
                    throw new FileNotFoundException("The source file does not exist.", oldFilePath);
                }

                p.MoveFile(oldFilePath, newFilePath);

                Console.WriteLine($"File renamed from '{oldFilePath}' to '{newFilePath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error renaming the file: {ex.Message}");
            }
        }

        /// <summary>
        /// Renames a directory from the specified old directory path to the specified new directory path.
        /// </summary>
        /// <param name="olddir">The path of the directory to be renamed.</param>
        /// <param name="newdir">The new path for the renamed directory.</param>
        public static void RenameDirectory(string olddir, string newdir)
        {
            try
            {
                var p = ProviderRegistry.For(olddir);

                if (!p.DirectoryExists(olddir) || p.DirectoryExists(newdir))
                {
                    MessageBox mb = new MessageBox("The source directory does not exist or the target directory already exists.");
                    mb.ShowDialog(GetMainWindow());
                }

                p.MoveDirectory(olddir, newdir);
            }
            catch (Exception ex)
            {
                MessageBox mb = new MessageBox($"Error renaming the directory: {ex.Message}");
                mb.ShowDialog(GetMainWindow());
            }
        }

        /// <summary>
        /// Replaces double backslashes with single backslashes in the given path.
        /// Appends a trailing backslash for Windows OS or a trailing forward slash for Unix/MacOSX OS if necessary.
        /// </summary>
        /// <param name="path">The original path.</param>
        /// <returns>The modified path.</returns>
        public static string MakePathENVSafe(string path)
        {
            string result = path.Replace(@"\\", @"\"); // get rid of double backslashes
            
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
        public static void PopulateFilePanel(TaiDataGrid ThePanel, string PATHNAME, bool ShowHidden, bool SortByName)
        {
            //LPgrid.PopulateGrid(PATHNAME);

            //var Directories = System.IO.Directory.EnumerateDirectories(PATHNAME);

            // using linq to sort the directories by name alphabetically
            
            if (PATHNAME == null || PATHNAME == "")
            {
                return;
            }
            
            
            try
            {
                var provider = ProviderRegistry.For(PATHNAME);

                var directoryEntries = provider.EnumerateDirectories(PATHNAME)
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ThePanel.SuspendRendering = true;
                ThePanel.Items.Clear();
                List<AFileEntry> FileList = new List<AFileEntry>();

                foreach (var entry in directoryEntries)
                {
                    try
                    {
                        if (entry.Flags.HasFlag(FileEntryFlags.System)) continue;
                        if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;

                        var ds = provider.EnumerateDirectories(entry.Path).Count();
                        var fs = provider.EnumerateFiles(entry.Path).Count();
                        long dirSize = provider.GetDirectorySize(entry.Path, recursive: false);

                        FileList.Add(new AFileEntry(entry.Name, dirSize, true, ds, fs, entry.AttributeString));
                    }
                    catch
                    {
                        try { FileList.Add(new AFileEntry(entry.Name, 0, true, 0, 0, "")); }
                        catch { }
                    }
                }

                var fileEntries = provider.EnumerateFiles(PATHNAME)
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var entry in fileEntries)
                {
                    try
                    {
                        if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;

                        string ft = entry.LastModified.ToShortDateString() + " " + entry.LastModified.ToShortTimeString();
                        FileList.Add(new AFileEntry(entry.Name, entry.Size, false, entry.AttributeString, ft));
                    }
                    catch { }
                }

                FileList = SortByName
                    ? FileList.OrderBy(fe => fe.Name).ToList()
                    : FileList.OrderBy(fe => fe.FileSize).ToList();

                ThePanel.Items = FileList.OfType<object>().ToList();
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);
                MB.ShowDialog(GetMainWindow());
            }

            ThePanel.SuspendRendering = false;
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
            
            
            try
            {
                var provider = ProviderRegistry.For(PATHNAME);

                var directoryEntries = provider.EnumerateDirectories(PATHNAME)
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                ThePanel.SuspendRendering = true;
                ThePanel.Items.Clear();
                List<Object> FileList = new List<Object>();

                foreach (var entry in directoryEntries)
                {
                    try
                    {
                        if (entry.Flags.HasFlag(FileEntryFlags.System)) continue;
                        if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;

                        var ds = provider.EnumerateDirectories(entry.Path).Count();
                        var fs = provider.EnumerateFiles(entry.Path).Count();
                        long dirSize = provider.GetDirectorySize(entry.Path, recursive: false);

                        FileList.Add(new AFileEntry(entry.Name, dirSize, true, ds, fs, entry.AttributeString));
                    }
                    catch
                    {
                        try { FileList.Add(new AFileEntry(entry.Name, 0, true, 0, 0, "")); }
                        catch { }
                    }
                }

                var fileEntries = provider.EnumerateFiles(PATHNAME)
                    .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var entry in fileEntries)
                {
                    try
                    {
                        if (!ShowHidden && entry.Flags.HasFlag(FileEntryFlags.Hidden)) continue;

                        string ft = entry.LastModified.ToShortDateString() + " " + entry.LastModified.ToShortTimeString();
                        FileList.Add(new AFileEntry(entry.Name, entry.Size, false, entry.AttributeString, ft));
                    }
                    catch { }
                }

                ThePanel.Items = FileList.OfType<object>().ToList();
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);
                MB.ShowDialog(GetMainWindow());
            }

            ThePanel.SuspendRendering = false;
        }

        /// <summary>
        /// Deletes a folder and its contents.
        /// </summary>
        /// <param name="rootPath">The root path of the folder to delete.</param>
        public static void DeleteFolder(string rootPath)
        {
            try
            {
                var p = ProviderRegistry.For(rootPath);
                if (p.DirectoryExists(rootPath))
                {
                    p.DeleteDirectory(rootPath, recursive: true);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);
                MB.ShowDialog(GetMainWindow());
            }
        }

        /// <summary>
        /// This method deletes a file at the specified root path.
        /// </summary>
        /// <param name="rootPath">The root path of the file to be deleted.</param>
        public static void DeleteFile(string rootPath)
        {
            try
            {
                var p = ProviderRegistry.For(rootPath);
                if (p.FileExists(rootPath))
                {
                    p.DeleteFile(rootPath);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);
                MB.ShowDialog(GetMainWindow());
            }
        }
    }
}
