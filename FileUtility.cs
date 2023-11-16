using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
//using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DirOpusReImagined
{
    public static class FileUtility
    {
        public static void CopyFileToFolder(string sourceFile, string targetFolder)
        {
            try
            {
                // Ensure that the target directory exists
                Directory.CreateDirectory(targetFolder);

                // Generate a target file path
                string targetFile = Path.Combine(targetFolder, Path.GetFileName(sourceFile));

                // Copy the file to the target directory
                File.Copy(sourceFile, targetFile, true); // Overwrite existing files

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

        public static void CopyDirectoryToFolder(string sourceDirectory, string targetDirectory)
        {
            try
            {
                // Create the target directory if it doesn't exist
                Directory.CreateDirectory(targetDirectory);

                // Get all files in the source directory and copy them to the target directory
                foreach (string file in Directory.GetFiles(sourceDirectory))
                {
                    string fileName = Path.GetFileName(file);
                    string targetFile = Path.Combine(targetDirectory, fileName);
                    File.Copy(file, targetFile, true);
                }

                // Recursively copy subdirectories
                foreach (string directory in Directory.GetDirectories(sourceDirectory))
                {
                    string dirName = Path.GetFileName(directory);
                    string targetDir = Path.Combine(targetDirectory, dirName);
                    CopyDirectoryToFolder(directory, targetDir);
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

        public static void MoveFile(string sourceFile, string targetDirectory)
        {
            try
            {
                // Ensure that the target directory exists
                Directory.CreateDirectory(targetDirectory);

                // Generate a target file path
                string targetFile = Path.Combine(targetDirectory, Path.GetFileName(sourceFile));

                // Move the file to the target directory
                File.Move(sourceFile, targetFile);

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

        public static void MoveDirectory(string sourceDirectory, string targetDirectory)
        {
            try
            {
                // Move the directory to the target directory
                Directory.Move(sourceDirectory, targetDirectory);

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

        public static void RenameFile(string oldFilePath, string newFilePath)
        {
            try
            {
                // Verify that the source file exists
                if (!File.Exists(oldFilePath))
                {
                    throw new FileNotFoundException("The source file does not exist.", oldFilePath);
                    // we need to handle this a little better
                }

                // Rename the file using File.Move
                File.Move(oldFilePath, newFilePath);

                Console.WriteLine($"File renamed from '{oldFilePath}' to '{newFilePath}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error renaming the file: {ex.Message}");
            }
        }
        
        public static void RenameDirectory(string olddir, string newdir)
        {
            try
            {
                // Verify that the source directory exists
                if (!Directory.Exists(olddir) || Directory.Exists(newdir))
                {
                    MessageBox mb = new MessageBox("The source directory does not exist or the target directory already exists.");
                    
                    mb.Show(null);
                    
                    //throw new DirectoryNotFoundException("The source directory does not exist.");
                    // we need to handle this a little better
                }

                // Rename the directory using Directory.Move
                Directory.Move(olddir, newdir);

                //Console.WriteLine($"Directory renamed from '{olddir}' to '{newdir}'");
            }
            catch (Exception ex)
            {
                MessageBox mb = new MessageBox($"Error renaming the directory: {ex.Message}");
                mb.Show(null);
                
                //Console.WriteLine($"Error renaming the directory: {ex.Message}");
            }
        }
        
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

        public static string FileNameMinusExtension(string path)
        {
            string result = Path.GetFileNameWithoutExtension(path);

            return result;
        }
        
        public static string FilenameExtension(string path)
        {
            string result = Path.GetExtension(path);

            return result;
        }
        
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
                
                var Directories = Directory.EnumerateDirectories(PATHNAME)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ThePanel.SuspendRendering = true;

            ThePanel.Items.Clear();
            List<Object> FileList = new List<Object>();

            foreach (string dir in Directories)
            {
                DirectoryInfo di = new DirectoryInfo(dir);
                try
                {

                    if (di.Attributes.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }

                    string flags = GetAbbreviatedAttributes(di.Attributes);
                                        
                    var ds = di.GetDirectories().GetUpperBound(0) + 1;
                    var fs = di.GetFiles().GetUpperBound(0) + 1;
                    
                    // if we are showing hidden files 
                    // and the flags contain the hidden flag
                    // then we add it to the list

                    if (ShowHidden) // Who cares show em all
                    {
                        FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs,flags));
                    }
                    else if (!ShowHidden && !flags.Contains(" H")) // if we are not showing hidden files and the flags do not contain the hidden flag
                    {
                        FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs,flags));
                    }
                    
                    //FileList.Add(new AFileEntry(di.Name, 0, true, ds, fs,flags));
                }
                catch (UnauthorizedAccessException)
                {
                    
                    try
                    {                    
                        FileList.Add(new AFileEntry(di.Name, 0, true, 0, 0,""));
                    }
                    catch
                    {

                    }
                }
                //var ds = di.GetDirectories().GetUpperBound(0);
                //var fs = di.GetFiles().GetUpperBound(0);

                //FileList.Add(new AFileEntry(di.Name, 0, true,ds,fs));
            }

            // Using Linq to sort the files alphabetically
            var files = Directory.EnumerateFiles(PATHNAME)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(); ;

            foreach (string file in files)
            {
                try
                {
                    FileInfo fi = new FileInfo(file);

                    FileAttributes fa = File.GetAttributes(fi.FullName);

                    string flags = GetAbbreviatedAttributes(fa);

                    string ft = fi.LastWriteTime.ToShortDateString() + " " + fi.LastWriteTime.ToShortTimeString();

                    if (ShowHidden) // Again who cares
                    {
                        FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false,flags,ft));
                    }
                    else if (!ShowHidden && !flags.Contains(" H")) // if we are not showing hidden files and the flags do not contain the hidden flag
                    {
                        FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false,flags,ft));
                    }
                    
                    //FileList.Add(new AFileEntry(fi.Name, (int)fi.Length, false,flags,ft));
                }
                catch
                {
                    
                }
            }

            ThePanel.Items = FileList.OfType<object>().ToList(); 


            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);

                MB.Show();
                
                // if (ThePanel.Name == "RPgrid")
                // {
                //     RPpath.Text = oldpath;
                // }
                // else
                // {
                //     LPpath.Text = oldpath;
                // }
                
            }
            
            
            ThePanel.SuspendRendering = false;
        }
        
        private static string GetAbbreviatedAttributes(FileAttributes attributes)
        {
            string abbreviatedAttributes = string.Empty;

            if ((attributes & FileAttributes.ReadOnly) != 0)
                abbreviatedAttributes += "RO ";
            else
                abbreviatedAttributes += "RW ";
            if ((attributes & FileAttributes.Hidden) != 0)
                abbreviatedAttributes += "H ";
            else
                abbreviatedAttributes += "V ";
            if ((attributes & FileAttributes.System) != 0)
                abbreviatedAttributes += "S ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Directory) != 0)
                abbreviatedAttributes += "D ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Archive) != 0)
                abbreviatedAttributes += "A ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Device) != 0)
                abbreviatedAttributes += "DEV ";
            else
            {
                abbreviatedAttributes += "    ";
            }
            if ((attributes & FileAttributes.Normal) != 0)
                abbreviatedAttributes += "N ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Temporary) != 0)
                abbreviatedAttributes += "T ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.SparseFile) != 0)
                abbreviatedAttributes += "SF ";
            else
            {
                abbreviatedAttributes += "   ";
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                abbreviatedAttributes += "RP ";
            else
            {
                abbreviatedAttributes += "   ";
            }
            if ((attributes & FileAttributes.Compressed) != 0)
                abbreviatedAttributes += "C ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Offline) != 0)
                abbreviatedAttributes += "O ";
            else
            {
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.NotContentIndexed) != 0)
                abbreviatedAttributes += "NCI ";
            else
            {
                
                abbreviatedAttributes += "  ";
            }
            if ((attributes & FileAttributes.Encrypted) != 0)
                abbreviatedAttributes += "E ";
            else
            { abbreviatedAttributes += "  "; }
            if ((attributes & FileAttributes.IntegrityStream) != 0)
                abbreviatedAttributes += "IS ";
            else
            { abbreviatedAttributes += "  "; }
            if ((attributes & FileAttributes.NoScrubData) != 0)
                abbreviatedAttributes += "NSD ";
            else
            {
                
                abbreviatedAttributes += "   ";
            }

            return abbreviatedAttributes.Trim();
        }

        public static void DeleteFolder(string rootPath)
        {
            try
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);

                MB.ShowDialog(null);
            }
            
        }

        public static void DeleteFile(string rootPath)
        {
            try
            {
                if (File.Exists(rootPath))
                {
                    File.Delete(rootPath);
                }
            }
            catch (Exception e)
            {
                MessageBox MB = new MessageBox(e.Message);

                MB.ShowDialog(null);
            }
        }
    }
}
