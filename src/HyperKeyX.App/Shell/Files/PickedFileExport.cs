using HyperKeyX.Application.Common;
using Windows.Storage;

namespace HyperKeyX.Shell.Files;

/// <summary>协调用户选定的文件导出；空选择是取消，其他失败保留原错误契约。</summary>
internal static class PickedFileExport
{
    /// <summary>选择器取消返回 null，且不执行可能保存草稿的导出回调。</summary>
    internal static async Task<OperationResult?> ExportAsync(
        Func<Task<StorageFile?>> pickFile, Func<Stream, Task<OperationResult>> export)
    {
        var file = await pickFile();
        if (file is null) return null;
        return await WriteAsync(file, export);
    }

    /// <summary>只修改事务视图；回调成功且提交完成后才报告成功，不退回路径覆盖。</summary>
    internal static async Task<OperationResult> WriteAsync(
        StorageFile file, Func<Stream, Task<OperationResult>> export)
        => await WriteAsync(() => OpenTransactionAsync(file), export);

    /// <summary>系统事务适配器；不根据文件路径猜测存储提供方能力。</summary>
    internal static async Task<IFileExportTransaction> OpenTransactionAsync(StorageFile file) =>
        new StorageFileExportTransaction(await file.OpenTransactedWriteAsync());

    /// <summary>统一事务生命周期；打开、导出、释放写流和提交任一失败均不能报告成功。</summary>
    internal static async Task<OperationResult> WriteAsync(
        Func<Task<IFileExportTransaction>> openTransaction, Func<Stream, Task<OperationResult>> export)
    {
        try
        {
            using var transaction = await openTransaction();
            // 回调可能自行释放流；克隆与事务主流共享内容但生命周期独立。
            OperationResult result;
            await using (var stream = transaction.OpenWriteStream())
            {
                stream.SetLength(0);
                result = await export(stream);
            }
            if (!result.Succeeded) return result;
            await transaction.CommitAsync();
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) { return OperationResult.Failure(exception.Message); }
    }

    /// <summary>主流归事务所有，返回的克隆可由既有宏导出调用方独立释放。</summary>
    private sealed class StorageFileExportTransaction(StorageStreamTransaction transaction) : IFileExportTransaction
    {
        public Stream OpenWriteStream() => transaction.Stream.CloneStream().AsStreamForWrite();
        public async Task CommitAsync() => await transaction.CommitAsync();
        public void Dispose() => transaction.Dispose();
    }
}

/// <summary>导出事务的内部存储边界：释放未提交事务应保留原内容。</summary>
internal interface IFileExportTransaction : IDisposable
{
    /// <summary>取得独立生命周期的事务写流。</summary>
    Stream OpenWriteStream();
    /// <summary>将完整内容提交到目标，失败必须向调用方传播。</summary>
    Task CommitAsync();
}
