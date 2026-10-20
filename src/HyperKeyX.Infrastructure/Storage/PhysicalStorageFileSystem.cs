namespace HyperKeyX.Infrastructure.Storage;

internal sealed class PhysicalStorageFileSystem : IStorageFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateFiles(string path) =>
        Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly).ToArray();

    public IReadOnlyList<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly).ToArray();

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;
}
