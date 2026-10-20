namespace HyperKeyX.Application.Updates;

/// <summary>已完整下载并校验的安装包；安装前仍需重新校验文件哈希。</summary>
public sealed record PreparedUpdate(string Version, string InstallerPath, string Sha256, long Bytes);
