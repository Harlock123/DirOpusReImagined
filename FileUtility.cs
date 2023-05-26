using System;
using System.IO;

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

    }
}
