[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-preview\.(0|[1-9]\d*))?$')]
    [string] $Version,

    [Parameter()]
    [string] $OutputDirectory = 'artifacts/zip'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutputDirectory = if ([IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDirectory -ne $artifactsRoot -and
    -not $resolvedOutputDirectory.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "输出目录必须位于仓库 artifacts 目录内：$artifactsRoot"
}

# 验证已有父目录不会通过符号链接或 junction 把发布文件写到 artifacts 之外。
function Assert-SafeOutputParents {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Target
    )

    $relativeTarget = [IO.Path]::GetRelativePath($Root, $Target)
    $segments = @($relativeTarget -split '[\\/]' | Where-Object { $_ -and $_ -ne '.' })
    $currentPath = $Root
    foreach ($segment in $segments) {
        if ((Test-Path -LiteralPath $currentPath -PathType Container) -and
            ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "输出父目录不能是重解析点：$currentPath"
        }

        $currentPath = Join-Path $currentPath $segment
    }

    if ((Test-Path -LiteralPath $currentPath -PathType Container) -and
        ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "输出父目录不能是重解析点：$currentPath"
    }
}

Assert-SafeOutputParents -Root $artifactsRoot -Target $resolvedOutputDirectory

$projectPath = Join-Path $repositoryRoot 'src\HyperKeyX.App\HyperKeyX.App.csproj'
$publishProfilePath = Join-Path $repositoryRoot 'src\HyperKeyX.App\Properties\PublishProfiles\win-x64.pubxml'
$stagingParent = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\releases\zip-staging'))
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $stagingParent $Version))
$publishDirectory = Join-Path $stagingRoot 'publish'
$zipPath = Join-Path $resolvedOutputDirectory "HyperKeyX-$Version-win-x64.zip"
$checksumPath = Join-Path $resolvedOutputDirectory 'SHA256SUMS.txt'

$stagingPrefix = $stagingParent.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $stagingRoot.StartsWith($stagingPrefix, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetRelativePath($stagingParent, $stagingRoot) -cne $Version) {
    throw "ZIP 暂存目录不在预期路径内：$stagingRoot"
}

Assert-SafeOutputParents -Root $stagingParent -Target $stagingRoot
$stagingRootPrefix = $stagingRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDirectory -eq $stagingRoot -or
    $resolvedOutputDirectory.StartsWith($stagingRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "输出目录不能位于当前 ZIP 暂存目录内：$resolvedOutputDirectory"
}

foreach ($targetPath in $zipPath, $checksumPath) {
    if (Test-Path -LiteralPath $targetPath) {
        throw "发布目标已存在，脚本不会覆盖：$targetPath"
    }
}

# 版本格式已经排除路径分隔符，并且绝对路径已经验证为专用 staging 的直接子目录。
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

[void](New-Item -ItemType Directory -Path $publishDirectory -Force)
[void](New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force)

Push-Location $repositoryRoot
try {
    Write-Host "Publishing HyperKeyX $Version for win-x64 ZIP..."
    & dotnet publish $projectPath `
        --configuration Release `
        --runtime win-x64 `
        --self-contained false `
        -maxcpucount:1 `
        --output $publishDirectory `
        "-p:PublishProfile=$publishProfilePath" `
        "-p:Version=$Version" `
        '-p:WindowsAppSDKSelfContained=false' `
        '-p:DebugSymbols=false' `
        '-p:DebugType=None'
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish 失败，退出代码：$LASTEXITCODE。"
    }
}
finally {
    Pop-Location
}

$requiredPublishPaths = @(
    'HyperKeyX.exe',
    'HyperKeyX.dll',
    'HyperKeyX.deps.json',
    'HyperKeyX.runtimeconfig.json',
    'HyperKeyX.pri',
    'HyperKeyX.Application.dll',
    'HyperKeyX.Domain.dll',
    'HyperKeyX.Infrastructure.dll',
    'LICENSE',
    'COPYING',
    'COPYING.LESSER',
    'Assets\Brand\HyperKeyX.ico',
    'ThirdPartyNotices\CommunityToolkit.Windows.MIT.txt'
)
foreach ($relativePath in $requiredPublishPaths) {
    $requiredPath = Join-Path $publishDirectory $relativePath
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "发布输出缺少 ZIP 必需内容：$requiredPath"
    }
}

$publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse -Force)
$forbiddenFiles = @($publishedFiles | Where-Object {
    $_.Extension -imatch '^\.(msi|cab|pdb)$' -or
    $_.Name -iin @(
        'coreclr.dll',
        'System.Private.CoreLib.dll',
        'Microsoft.UI.Xaml.dll',
        'Microsoft.WindowsAppRuntime.dll',
        'onnxruntime.dll',
        'DirectML.dll'
    ) -or
    $_.Name -imatch '^(onnx|directml)'
})
if ($forbiddenFiles.Count -ne 0) {
    $forbiddenRelativePaths = @($forbiddenFiles | ForEach-Object {
        [IO.Path]::GetRelativePath($publishDirectory, $_.FullName)
    })
    throw "发布输出包含 ZIP 禁止文件：$($forbiddenRelativePaths -join ', ')"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $publishDirectory,
    $zipPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumPath -Value "$zipHash  $([IO.Path]::GetFileName($zipPath))" -Encoding ASCII

Write-Host "ZIP created: $zipPath"
Write-Output ([pscustomobject]@{
    ZipPath = $zipPath
    ChecksumPath = $checksumPath
    SemanticVersion = $Version
    PublishPath = $publishDirectory
    Sha256 = $zipHash
})
