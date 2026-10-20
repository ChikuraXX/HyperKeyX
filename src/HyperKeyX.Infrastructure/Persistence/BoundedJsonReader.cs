using System.Text;

namespace HyperKeyX.Infrastructure.Persistence;

internal static class BoundedJsonReader
{
    public static string Read(Stream source, long maximumBytes) =>
        ReadAsync(source, maximumBytes, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<string> ReadAsync(Stream source, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBytes, int.MaxValue);
        cancellationToken.ThrowIfCancellationRequested();
        if (source.CanSeek && source.Length - source.Position > maximumBytes)
            throw new InvalidDataException("The JSON input exceeds its byte limit.");

        using var bounded = new MemoryStream();
        var buffer = new byte[8_192];
        while (true)
        {
            // 不可寻址流最多多读一个哨兵字节，不能为判断超限读完整个输入。
            var remaining = maximumBytes - bounded.Length + 1;
            var requested = (int)Math.Min(buffer.Length, remaining);
            var count = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                break;
            if (bounded.Length + count > maximumBytes)
                throw new InvalidDataException("The JSON input exceeds its byte limit.");
            bounded.Write(buffer, 0, count);
        }
        bounded.Position = 0;
        using var reader = new StreamReader(bounded, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}
