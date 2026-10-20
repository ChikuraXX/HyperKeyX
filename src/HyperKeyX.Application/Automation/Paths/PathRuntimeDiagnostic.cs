using System.Text.Json;
using System.Text.Json.Serialization;
using HyperKeyX.Domain.Automation.Paths;

namespace HyperKeyX.Application.Automation.Paths;

/// <summary>沿既有错误通知通道传递可定位代码，展示层决定语言，不解析系统异常文本。</summary>
public static class PathRuntimeDiagnostic
{
    private const string Prefix = "MousePath:";
    private static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter<PathIssueCode>() } };
    public static string Encode(IEnumerable<PathIssue> issues) => Prefix + JsonSerializer.Serialize(issues.Take(16).ToArray(), Options);
    public static bool TryDecode(string message, out IReadOnlyList<PathIssue> issues)
    {
        issues = [];
        if (!message.StartsWith(Prefix, StringComparison.Ordinal) || message.Length > 16_384) return false;
        try
        {
            var decoded = JsonSerializer.Deserialize<PathIssue[]>(message[Prefix.Length..], Options);
            if (decoded is null || decoded.Length is 0 or > 16 || decoded.Any(i => i is null || !Enum.IsDefined(i.Code))) return false;
            issues = Array.AsReadOnly(decoded); return true;
        }
        catch (JsonException) { return false; }
    }
}
