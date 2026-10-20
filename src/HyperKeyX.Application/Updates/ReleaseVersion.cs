using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace HyperKeyX.Application.Updates;

/// <summary>发布语义版本：同一数字版本下，正式版高于预览版，预览数字标识按数值比较。</summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    private readonly string[] _prerelease;

    /// <summary>保存校验后的版本及预览标识，构建元数据不参与先后排序。</summary>
    private ReleaseVersion(string value, Version number, string[] prerelease)
    {
        Value = value;
        Number = new(number.Major, number.Minor, Math.Max(0, number.Build), Math.Max(0, number.Revision));
        _prerelease = prerelease;
    }

    /// <summary>不含 v 前缀的发布版本，保留预览标识及构建元数据。</summary>
    public string Value { get; }
    /// <summary>规范化的四段数字部分。</summary>
    public Version Number { get; }
    /// <summary>是否携带预览标识。</summary>
    public bool IsPrerelease => _prerelease.Length > 0;

    /// <summary>接受三段或兼容旧版的四段数字版本；标签最长 128 字符，拒绝预览数字的前导零。</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out ReleaseVersion? result)
    {
        result = null;
        if (text is null || text.Length > 128) return false;
        var match = Regex.Match(text,
            @"^[vV]?(?<core>(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)(?:\.(?:0|[1-9]\d*))?)(?:-(?<pre>[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$",
            RegexOptions.CultureInvariant);
        if (!match.Success || !Version.TryParse(match.Groups["core"].Value, out var number)) return false;
        var prerelease = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
        if (prerelease.Any(part => IsNumeric(part) && part.Length > 1 && part[0] == '0')) return false;
        result = new(text.TrimStart('v', 'V'), number, prerelease);
        return true;
    }

    /// <summary>比较发布先后，忽略构建元数据；空对象低于任何有效版本。</summary>
    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var core = Number.CompareTo(other.Number);
        if (core != 0) return core;
        if (!IsPrerelease || !other.IsPrerelease)
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        for (var i = 0; i < Math.Min(_prerelease.Length, other._prerelease.Length); i++)
        {
            var left = _prerelease[i];
            var right = other._prerelease[i];
            var leftNumeric = IsNumeric(left);
            var rightNumeric = IsNumeric(right);
            var comparison = leftNumeric && rightNumeric
                ? left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right)
                : leftNumeric != rightNumeric ? leftNumeric ? -1 : 1 : string.CompareOrdinal(left, right);
            if (comparison != 0) return comparison;
        }
        return _prerelease.Length.CompareTo(other._prerelease.Length);
    }

    /// <summary>使用 ASCII 数字规则，避免区域设置改变版本标识的语义。</summary>
    private static bool IsNumeric(string part) => part.All(character => character is >= '0' and <= '9');
}
