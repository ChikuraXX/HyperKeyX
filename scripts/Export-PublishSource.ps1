[CmdletBinding()]
param(
    # 导出目录必须位于仓库 artifacts 目录内；已有目标会被拒绝，避免覆盖用户文件。
    [string]$OutputDirectory = 'artifacts/publish-source'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = if ([IO.Path]::IsPathFullyQualified($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}

$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "输出目录必须位于仓库 artifacts 目录内：$artifactsRoot"
}

# 已存在的父目录不得是重解析点，防止 junction 或符号链接把写入重定向到 artifacts 之外。
$relativeOutput = [IO.Path]::GetRelativePath($artifactsRoot, $resolvedOutput)
$outputSegments = $relativeOutput -split '[\\/]'
$currentParent = $artifactsRoot
for ($index = 0; $index -lt $outputSegments.Count; $index++) {
    if ((Test-Path -LiteralPath $currentParent -PathType Container) -and
        ((Get-Item -LiteralPath $currentParent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "输出父目录不能是重解析点：$currentParent"
    }

    $currentParent = Join-Path $currentParent $outputSegments[$index]
}

$archivePath = "$resolvedOutput.zip"
if ((Test-Path -LiteralPath $resolvedOutput) -or (Test-Path -LiteralPath $archivePath)) {
    throw "导出目标已存在，脚本不会清空或覆盖：$resolvedOutput"
}

$requiredFiles = @(
    '.editorconfig'
    '.gitignore'
    '.github/workflows/release.yml'
    'HyperKeyX.slnx'
    'README.md'
    'README.zh-CN.md'
    'LICENSE'
    'COPYING'
    'COPYING.LESSER'
    'scripts/Build-Zip.ps1'
    'scripts/Export-PublishSource.ps1'
)
$requiredDirectories = @(
    'src'
    'ThirdPartyNotices'
    'docs/screenshots'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Leaf)) {
        throw "缺少发布源码所需文件：$relativePath"
    }
}

foreach ($relativePath in $requiredDirectories) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath) -PathType Container)) {
        throw "缺少发布源码所需目录：$relativePath"
    }
}

$excludedDirectoryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('bin', 'obj', '.vs', '.git', '.agents', 'graphify-out', 'artifacts', 'TestResults', 'publish') |
    ForEach-Object { [void]$excludedDirectoryNames.Add($_) }

$excludedExtensions = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@('.user', '.suo', '.userosscache', '.log', '.trx', '.coverage', '.coveragexml', '.cache', '.pdb', '.msi', '.nupkg', '.wixobj', '.wixpdb', '.zip') |
    ForEach-Object { [void]$excludedExtensions.Add($_) }

# 判断树中的文件是否属于可发布源码；保留构建会读取的 manifest、图标、图片、JSON 和 PublishProfiles。
function Test-PublishSourceFile {
    param(
        [Parameter(Mandatory)]
        [IO.FileInfo]$File,

        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    if (($File.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        return $false
    }

    $segments = $RelativePath -split '[\\/]'
    foreach ($segment in $segments[0..([Math]::Max(0, $segments.Count - 2))]) {
        if ($excludedDirectoryNames.Contains($segment)) {
            return $false
        }
    }

    if ($excludedExtensions.Contains($File.Extension)) {
        return $false
    }

    return -not $File.Name.EndsWith('.sln.docstates', [StringComparison]::OrdinalIgnoreCase)
}

# 复制单个白名单文件，并按需创建父目录。
function Copy-PublishFile {
    param(
        [Parameter(Mandatory)]
        [string]$RelativePath
    )

    $sourcePath = Join-Path $repositoryRoot $RelativePath
    $destinationPath = Join-Path $resolvedOutput $RelativePath
    $destinationParent = Split-Path -Parent $destinationPath
    [void](New-Item -ItemType Directory -Path $destinationParent -Force)
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath
}

# 复制一个白名单目录，同时过滤递归构建输出和开发缓存。
function Copy-PublishTree {
    param(
        [Parameter(Mandatory)]
        [string]$RelativeRoot
    )

    $sourceRoot = Join-Path $repositoryRoot $RelativeRoot
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -File -Recurse -Force) {
        $childPath = [IO.Path]::GetRelativePath($sourceRoot, $file.FullName)
        if (-not (Test-PublishSourceFile -File $file -RelativePath $childPath)) {
            continue
        }

        Copy-PublishFile -RelativePath (Join-Path $RelativeRoot $childPath)
    }
}

[void](New-Item -ItemType Directory -Path $resolvedOutput)

foreach ($relativePath in $requiredFiles) {
    Copy-PublishFile -RelativePath $relativePath
}

foreach ($relativePath in $requiredDirectories) {
    Copy-PublishTree -RelativeRoot $relativePath
}

# 发布副本不带 tests，因此同步从副本解决方案移除测试项目；原工作树文件保持不变。
$solutionPath = Join-Path $resolvedOutput 'HyperKeyX.slnx'
[xml]$solution = Get-Content -LiteralPath $solutionPath -Raw -Encoding UTF8
foreach ($project in @($solution.SelectNodes('//Project'))) {
    $projectPath = [string]$project.Path
    if ($projectPath.Replace('\', '/').StartsWith('tests/', [StringComparison]::OrdinalIgnoreCase)) {
        [void]$project.ParentNode.RemoveChild($project)
    }
}

$xmlSettings = [Xml.XmlWriterSettings]::new()
$xmlSettings.Encoding = [Text.UTF8Encoding]::new($false)
$xmlSettings.Indent = $true
$xmlSettings.NewLineChars = "`n"
$xmlSettings.NewLineHandling = [Xml.NewLineHandling]::Replace
$xmlWriter = [Xml.XmlWriter]::Create($solutionPath, $xmlSettings)
try {
    $solution.Save($xmlWriter)
}
finally {
    $xmlWriter.Dispose()
}

$manifestPath = Join-Path $resolvedOutput 'publish-source.manifest.json'
$manifestFiles = @(
    Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse -Force |
        Sort-Object { [IO.Path]::GetRelativePath($resolvedOutput, $_.FullName) } |
        ForEach-Object {
            $relativePath = [IO.Path]::GetRelativePath($resolvedOutput, $_.FullName).Replace('\', '/')
            [ordered]@{
                path = $relativePath
                length = $_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    fileCount = $manifestFiles.Count
    files = $manifestFiles
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory(
    $resolvedOutput,
    $archivePath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

[pscustomobject]@{
    Directory = $resolvedOutput
    Archive = $archivePath
    Manifest = $manifestPath
    FileCount = $manifestFiles.Count
}
