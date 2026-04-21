using System.Collections.Generic;
using System.IO;

namespace DirOpusReImagined.FileSystem;

public interface IFileProvider
{
    bool CanHandle(string path);

    bool FileExists(string path);
    bool DirectoryExists(string path);

    FileEntry? Stat(string path);

    IEnumerable<FileEntry> EnumerateDirectories(string path);
    IEnumerable<FileEntry> EnumerateFiles(string path);

    Stream OpenRead(string path);
    Stream OpenWrite(string path);

    void CreateDirectory(string path);

    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);

    void CopyFile(string src, string dst, bool overwrite);
    void MoveFile(string src, string dst);
    void MoveDirectory(string src, string dst);

    long GetDirectorySize(string path, bool recursive);
}
