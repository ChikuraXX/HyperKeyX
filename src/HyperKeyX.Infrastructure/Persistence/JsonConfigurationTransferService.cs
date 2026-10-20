using System.Text;
using HyperKeyX.Application.Abstractions.Persistence;
using HyperKeyX.Application.Common;
using HyperKeyX.Domain.Configuration;
using HyperKeyX.Domain.Automation.Paths;
using HyperKeyX.Domain.Profiles;

namespace HyperKeyX.Infrastructure.Persistence;

/// <summary>
/// 通过调用方持有的流传输完整配置文档，不访问配置文件或运行时状态。
/// </summary>
public sealed class JsonConfigurationTransferService : IConfigurationTransferService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(
        encoderShouldEmitUTF8Identifier: false);

    public async Task<OperationResult<int>> ExportAsync(
        Stream destination,
        AutomationConfigurationDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(document);
            cancellationToken.ThrowIfCancellationRequested();

            // 序列化与返回计数必须来自同一快照，避免调用方并发修改文档。
            var snapshot = document.DeepClone();
            var json = ConfigurationDocumentJsonSerializer.Serialize(snapshot);
            var profileCount = ProfileConfigurationNormalizer
                .NormalizeForDocumentStorage(snapshot.Profiles ?? [])
                .Count;
            var bytes = Utf8WithoutBom.GetBytes(json);
            await destination.WriteAsync(bytes, cancellationToken);
            await destination.FlushAsync(cancellationToken);
            return OperationResult<int>.Success(profileCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult<int>.Failure(exception.Message);
        }
    }

    public async Task<OperationResult<ConfigurationImportResult>> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(source);
            cancellationToken.ThrowIfCancellationRequested();

            var json = await BoundedJsonReader.ReadAsync(
                source, MousePathLimits.MaximumDocumentBytes, cancellationToken);
            var parsed = ConfigurationDocumentJsonSerializer.Deserialize(json);
            if (!parsed.Succeeded)
            {
                return OperationResult<ConfigurationImportResult>.Failure(
                    parsed.ErrorMessage ?? "The configuration could not be imported.");
            }

            // 导入只产生候选草稿，持久化必须由上层在用户确认后显式执行。
            return OperationResult<ConfigurationImportResult>.Success(new(
                parsed.Value.Document,
                parsed.Value.WasMigrated,
                RequiresExplicitSave: true));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return OperationResult<ConfigurationImportResult>.Failure(
                exception.Message);
        }
    }
}
