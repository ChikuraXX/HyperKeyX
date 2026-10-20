namespace HyperKeyX.Infrastructure.Storage;

internal interface IStorageFileSystem
{
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateFiles(string path);
    IReadOnlyList<string> EnumerateDirectories(string path);
    FileAttributes GetAttributes(string path);
    long GetFileLength(string path);
}
